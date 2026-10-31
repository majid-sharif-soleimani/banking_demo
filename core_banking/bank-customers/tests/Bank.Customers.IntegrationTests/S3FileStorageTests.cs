using Amazon.Runtime;
using Amazon.S3;
using Bank.Customers.Api.Files;
using Microsoft.Extensions.Options;
using Testcontainers.LocalStack;

namespace Bank.Customers.IntegrationTests;

/// <summary>
/// S3FileStorage against LocalStack, an S3 emulator in a container: the same API calls the instance makes to S3 on AWS,
/// with dummy credentials. LocalStack 4.9 is the last image that runs without an account token.
/// </summary>
public sealed class S3FileStorageTests : IAsyncLifetime
{
    private readonly LocalStackContainer localStack = new LocalStackBuilder("localstack/localstack:4.9").Build();
    private AmazonS3Client s3 = null!;
    private S3FileStorage storage = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await localStack.StartAsync(Ct);
        s3 = new AmazonS3Client(new BasicAWSCredentials("test", "test"),
            new AmazonS3Config { ServiceURL = localStack.GetConnectionString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await s3.PutBucketAsync("bank-files", Ct);
        storage = new S3FileStorage(s3, Options.Create(new FileStorageOptions { Provider = "S3", Bucket = "bank-files", Prefix = "files/" }));
    }

    public async ValueTask DisposeAsync()
    {
        s3?.Dispose();
        await localStack.DisposeAsync();
    }

    [Fact]
    public async Task A_saved_file_can_be_read_back_and_deleted()
    {
        await storage.SaveAsync("customers/c1/i1", new MemoryStream("salary statement"u8.ToArray()), Ct);

        (await storage.ExistsAsync("customers/c1/i1", Ct)).ShouldBeTrue();
        using (var read = new StreamReader(await storage.OpenReadAsync("customers/c1/i1", Ct)))
        {
            (await read.ReadToEndAsync(Ct)).ShouldBe("salary statement");
        }

        (await s3.GetObjectMetadataAsync("bank-files", "files/customers/c1/i1", Ct)).ContentLength.ShouldBe(16);
        await storage.DeleteAsync("customers/c1/i1", Ct);
        (await storage.ExistsAsync("customers/c1/i1", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_file_is_FileNotFound() =>
        await Should.ThrowAsync<FileNotFoundException>(() => storage.OpenReadAsync("customers/none", Ct));

    [Theory]
    [InlineData("../backups/db.dump")]
    [InlineData("customers//x")]
    [InlineData("/customers/x")]
    public async Task A_path_that_could_leave_the_prefix_is_refused(string path) =>
        await Should.ThrowAsync<ArgumentException>(() => storage.ExistsAsync(path, Ct));
}
