namespace loxxking_backend_clean.Application.Common.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken cancellationToken);

    /// <summary>Removes a file this service uploaded. A missing file is not an error.</summary>
    Task DeleteAsync(string fileUrl, CancellationToken cancellationToken);

    /// <summary>Reads back a file this service uploaded; null when it is not there (or is not one of ours).</summary>
    Task<Stream?> OpenReadAsync(string fileUrl, CancellationToken cancellationToken);
}
