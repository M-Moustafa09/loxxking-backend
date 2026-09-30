using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace loxxking_backend_clean.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly string _baseUrl;

    public LocalFileStorageService(IWebHostEnvironment env, IConfiguration configuration)
    {
        _env = env;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5050";
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, string folder, CancellationToken cancellationToken)
    {
        if (stream is null || stream.Length == 0)
            throw new ArgumentException("File is required", nameof(stream));

        var webRoot = _env.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        }

        var uploadsFolder = Path.Combine(webRoot, "uploads", folder);
        Directory.CreateDirectory(uploadsFolder);

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = (contentType ?? "").ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/jpeg" or "image/jpg" => ".jpg",
                "audio/webm" => ".webm",
                "audio/mp4" or "audio/m4a" => ".m4a",
                "audio/ogg" => ".ogg",
                "audio/wav" or "audio/x-wav" => ".wav",
                "audio/mpeg" or "audio/mp3" => ".mp3",
                _ => ".jpg"
            };
        }

        var uniqueFileName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        var baseUri = string.IsNullOrWhiteSpace(_baseUrl) ? "http://localhost:5050" : _baseUrl.TrimEnd('/');
        return $"{baseUri}/uploads/{folder}/{uniqueFileName}";
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return Task.CompletedTask;

        try
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            var pathOnly = fileUrl;
            if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
            {
                pathOnly = uri.AbsolutePath;
            }

            var relativePath = pathOnly.TrimStart('/');
            var fullPath = Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete local file: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string fileUrl, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            var pathOnly = Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : fileUrl;
            var relativePath = Uri.UnescapeDataString(pathOnly).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

            // Only what UploadAsync wrote: the URL is read from the database, so it must not lead
            // anywhere else on the disk.
            var uploadsRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads")) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));

            if (fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            }
        }

        return Task.FromResult(stream);
    }
}
