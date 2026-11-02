using Bank.Common;
using Microsoft.Extensions.Options;

namespace Bank.Customers.Api.Files;

public sealed class FileStorageOptions
{
    public const string Section = "Files";

    /// <summary>Folder for uploaded files in development (design plan ADR 18; S3 in stage 8). Gitignored.</summary>
    public string Root { get; set; } = "./.data/files";
}

/// <summary>IFileStorage on the local file system. Paths are relative and may not leave the root.</summary>
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    public async Task SaveAsync(string path, Stream content, CancellationToken ct)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct) =>
        File.Exists(Resolve(path)) ? Task.FromResult<Stream>(File.OpenRead(Resolve(path))) : throw new FileNotFoundException(path);

    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(File.Exists(Resolve(path)));

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        File.Delete(Resolve(path));
        return Task.CompletedTask;
    }

    private string Resolve(string path)
    {
        var root = Path.GetFullPath(options.Value.Root);
        var full = Path.GetFullPath(Path.Combine(root, path));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new ArgumentException("The path leaves the storage root.", nameof(path));
    }
}
