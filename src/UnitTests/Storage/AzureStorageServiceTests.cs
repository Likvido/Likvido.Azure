using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Likvido.Azure.Storage;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Storage;

public class AzureStorageServiceTests
{
    private const string Key = "cases/ref-1/document.pdf";

    private readonly Mock<BlobContainerClient> containerClient = new();
    private readonly Dictionary<string, Mock<BlobClient>> blobs = new();
    private readonly List<string> uploadedNames = new();
    private readonly List<BlobUploadOptions> uploadOptions = new();

    [Theory]
    [InlineData(400, "InvalidHeaderValue", true)]
    [InlineData(400, "InvalidHeaderValue", false)]
    [InlineData(403, "AuthorizationFailure", true)]
    [InlineData(403, "AuthorizationFailure", false)]
    [InlineData(404, "ContainerNotFound", false)]
    [InlineData(500, "InternalError", true)]
    [InlineData(503, "ServerBusy", false)]
    public async Task SetAsync_WhenUploadFails_ShouldThrow(int status, string errorCode, bool overwrite)
    {
        SetupUpload(_ => throw new RequestFailedException(status, "failed", errorCode, null));
        var service = CreateService();

        var ex = await Should.ThrowAsync<RequestFailedException>(() => service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: overwrite));

        ex.Status.ShouldBe(status);
        uploadedNames.ShouldBe([Key]);
    }

    [Fact]
    public async Task SetAsync_WhenBlobAlreadyExistsAndOverwriteIsFalse_ShouldUploadWithNumberedName()
    {
        SetupUpload(name => name == Key ? throw BlobAlreadyExists() : null);
        var service = CreateService();

        var uri = await service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: false);

        uploadedNames.ShouldBe([Key, "cases/ref-1/document(1).pdf"]);
        uri.ShouldBe(UriFor("cases/ref-1/document(1).pdf"));
    }

    [Fact]
    public async Task SetAsync_WhenSeveralNumberedNamesExist_ShouldUseTheFirstFreeOne()
    {
        SetupUpload(name => name != "cases/ref-1/document(3).pdf" ? throw BlobAlreadyExists() : null);
        var service = CreateService();

        var uri = await service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: false);

        uploadedNames.ShouldBe([Key, "cases/ref-1/document(1).pdf", "cases/ref-1/document(2).pdf", "cases/ref-1/document(3).pdf"]);
        uri.ShouldBe(UriFor("cases/ref-1/document(3).pdf"));
    }

    [Fact]
    public async Task SetAsync_WhenRetrying_ShouldUploadFromTheStartOfTheStream()
    {
        var positions = new List<long>();
        var content = new MemoryStream([1, 2, 3]);
        SetupUpload(name =>
        {
            positions.Add(content.Position);
            content.Position = content.Length;
            return name == Key ? throw BlobAlreadyExists() : null;
        });
        var service = CreateService();

        await service.SetAsync(Key, content, overwrite: false);

        positions.ShouldBe([0, 0]);
    }

    [Fact]
    public async Task SetAsync_WhenConflictAndOverwriteIsTrue_ShouldThrow()
    {
        SetupUpload(_ => throw BlobAlreadyExists());
        var service = CreateService();

        var ex = await Should.ThrowAsync<RequestFailedException>(() => service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: true));

        ex.Status.ShouldBe(409);
        uploadedNames.ShouldBe([Key]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetAsync_WhenConflictIsNotBlobAlreadyExists_ShouldThrow(bool overwrite)
    {
        SetupUpload(_ => throw new RequestFailedException(409, "lease", "LeaseIdMissing", null));
        var service = CreateService();

        var ex = await Should.ThrowAsync<RequestFailedException>(() => service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: overwrite));

        ex.ErrorCode.ShouldBe("LeaseIdMissing");
        uploadedNames.ShouldBe([Key]);
    }

    [Fact]
    public async Task SetAsync_WhenEveryNumberedNameExists_ShouldStopAtTheCapAndThrow()
    {
        SetupUpload(_ => throw BlobAlreadyExists());
        var service = CreateService();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: false));

        ex.InnerException.ShouldBeOfType<RequestFailedException>();
        uploadedNames.Count.ShouldBe(AzureStorageService.MaxDuplicateSuffix + 1);
        uploadedNames[^1].ShouldBe($"cases/ref-1/document({AzureStorageService.MaxDuplicateSuffix}).pdf");
    }

    [Fact]
    public async Task SetAsync_WhenUploadSucceeds_ShouldReturnTheBlobUri()
    {
        SetupUpload(_ => null);
        var service = CreateService();

        var uri = await service.SetAsync(Key, new MemoryStream([1, 2, 3]));

        uri.ShouldBe(UriFor(Key));
        uploadOptions.Single().Conditions.ShouldBeNull();
    }

    [Fact]
    public async Task SetAsync_WhenOverwriteIsFalse_ShouldUploadOnlyIfNoBlobExists()
    {
        SetupUpload(_ => null);
        var service = CreateService();

        await service.SetAsync(Key, new MemoryStream([1, 2, 3]), overwrite: false);

        uploadOptions.Single().Conditions.IfNoneMatch.ShouldBe(ETag.All);
    }

    [Fact]
    public async Task SetAsync_WithMetadataAndFriendlyName_ShouldSendThemWithTheUploadOnly()
    {
        SetupUpload(name => name == Key ? throw BlobAlreadyExists() : null);
        var metadata = new Dictionary<string, string> { ["source"] = "test" };
        var service = CreateService();

        await service.SetAsync(Key, new MemoryStream([1, 2, 3]), friendlyName: "Søren's file.pdf", overwrite: false, metadata: metadata);

        var options = uploadOptions[^1];
        options.Metadata.ShouldBe(metadata);
        options.HttpHeaders.ContentDisposition.ShouldBe($"attachment; filename={FileNameSanitizer.Sanitize("Søren's file.pdf")}");
        options.HttpHeaders.ContentType.ShouldBe("application/octet-stream");
        foreach (var blob in blobs.Values)
        {
            blob.Verify(b => b.SetMetadataAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
            blob.Verify(b => b.SetHttpHeadersAsync(It.IsAny<BlobHttpHeaders>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
            blob.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private AzureStorageService CreateService() => new(containerClient.Object, null!);

    private static Uri UriFor(string name) => new($"https://account.blob.core.windows.net/container/{name}");

    private static RequestFailedException BlobAlreadyExists() =>
        new(409, "exists", BlobErrorCode.BlobAlreadyExists.ToString(), null);

    // onUpload receives the blob name and throws to simulate a failed upload.
    private void SetupUpload(Func<string, object?> onUpload)
    {
        containerClient
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns((string name) =>
            {
                if (!blobs.TryGetValue(name, out var blob))
                {
                    blob = new Mock<BlobClient>();
                    blob.SetupGet(b => b.Uri).Returns(UriFor(name));
                    blob
                        .Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
                        .Returns((Stream _, BlobUploadOptions options, CancellationToken _) =>
                        {
                            uploadedNames.Add(name);
                            uploadOptions.Add(options);
                            onUpload(name);
                            return Task.FromResult(Mock.Of<Response<BlobContentInfo>>());
                        });
                    blobs[name] = blob;
                }

                return blob.Object;
            });
    }
}
