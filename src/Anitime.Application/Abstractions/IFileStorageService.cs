namespace Anitime.Application.Abstractions;

public interface IFileStorageService
{
    Task<string> UploadAsync(string objectName, Stream stream, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string? objectName, CancellationToken cancellationToken);
    Task DeleteAsync(string? objectName, CancellationToken cancellationToken);
}
