namespace Bank.Common;

/// <summary>Stores uploaded documents: the local file system in development, S3 on AWS.</summary>
public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, CancellationToken ct);

    /// <summary>Opens a stored file; throws FileNotFoundException when there is none.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);

    Task<bool> ExistsAsync(string path, CancellationToken ct);

    Task DeleteAsync(string path, CancellationToken ct);
}
