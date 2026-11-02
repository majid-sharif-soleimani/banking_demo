using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Bank.Common;
using Microsoft.Extensions.Options;

namespace Bank.Customers.Api.Files;

/// <summary>
/// IFileStorage on S3 (design plan section 9: files on S3 on AWS). Paths become keys under <see cref="FileStorageOptions.Prefix"/>.
/// The client takes its credentials from the default chain: the EC2 instance role on AWS, never keys in configuration.
/// </summary>
public sealed class S3FileStorage(IAmazonS3 s3, IOptions<FileStorageOptions> options) : IFileStorage
{
    public async Task SaveAsync(string path, Stream content, CancellationToken ct)
    {
        // The upload stream may not be seekable; S3 needs the length, so buffer it (uploads are at most 10 MB).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        await s3.PutObjectAsync(new PutObjectRequest { BucketName = options.Value.Bucket, Key = Key(path), InputStream = buffer }, ct);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await s3.GetObjectAsync(options.Value.Bucket, Key(path), ct);
            var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, ct);
            copy.Position = 0;
            return copy;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException(path);
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        try
        {
            await s3.GetObjectMetadataAsync(options.Value.Bucket, Key(path), ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task DeleteAsync(string path, CancellationToken ct) => s3.DeleteObjectAsync(options.Value.Bucket, Key(path), ct);

    private string Key(string path) =>
        path.Split('/').Any(part => part is "" or "." or "..")
            ? throw new ArgumentException("The path must be relative, without empty, '.' or '..' parts.", nameof(path))
            : options.Value.Prefix + path;
}
