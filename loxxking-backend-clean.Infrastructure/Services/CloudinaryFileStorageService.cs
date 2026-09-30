using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace loxxking_backend_clean.Infrastructure.Services;

public class CloudinaryFileStorageService : IFileStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly string _cloudName;
    private readonly int _maxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private static readonly HttpClient Downloads = new() { Timeout = TimeSpan.FromSeconds(30) };

    public CloudinaryFileStorageService(IConfiguration configuration)
    {
        var cloudName = configuration["Cloudinary:CloudName"] 
            ?? throw new InvalidOperationException("Cloudinary:CloudName is not configured");
        var apiKey = configuration["Cloudinary:ApiKey"] 
            ?? throw new InvalidOperationException("Cloudinary:ApiKey is not configured");
        var apiSecret = configuration["Cloudinary:ApiSecret"] 
            ?? throw new InvalidOperationException("Cloudinary:ApiSecret is not configured");

        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account);
        _cloudName = cloudName;
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, string folder, CancellationToken cancellationToken)
    {
        if (stream is null || stream.Length == 0)
            throw new ArgumentException("File is required", nameof(stream));

        if (stream.Length > _maxFileSizeBytes)
            throw new InvalidOperationException($"File size exceeds maximum allowed ({_maxFileSizeBytes / 1024 / 1024}MB)");

        var allowedContentTypes = new[] { "image/png", "image/jpeg", "image/jpg", "image/webp" };
        if (!allowedContentTypes.Contains(contentType.ToLowerInvariant()))
            throw new InvalidOperationException("Only image files (PNG, JPG, WEBP) are allowed");

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, stream),
            Folder = folder,
            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
        
        if (uploadResult.Error is not null)
            throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");

        return uploadResult.SecureUrl.AbsoluteUri;
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return;

        try
        {
            var uri = new Uri(fileUrl);
            var path = uri.AbsolutePath;
            var parts = path.Split('/');
            var fileName = parts.Last();
            var publicId = string.Join("/", parts.Skip(1).Take(parts.Length - 2)) + "/" + fileName.Split('.')[0];

            var deleteParams = new DeletionParams(publicId);
            await _cloudinary.DestroyAsync(deleteParams);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete file from Cloudinary: {ex.Message}");
        }
    }

    public async Task<Stream?> OpenReadAsync(string fileUrl, CancellationToken cancellationToken)
    {
        // Only our own uploads: never fetch an arbitrary address read from the database.
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith($"/{_cloudName}/", StringComparison.Ordinal))
        {
            return null;
        }

        using var response = await Downloads.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }
}
