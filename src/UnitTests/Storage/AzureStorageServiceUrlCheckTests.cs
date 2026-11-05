using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Likvido.Azure.Storage;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Storage;

public class AzureStorageServiceUrlCheckTests
{
    private const string AccountKey = "dGVzdC1rZXktZm9yLXVuaXQtdGVzdHMtb25seQ==";
    private const string OwnContainerUrl = "https://account.blob.core.windows.net/likvido";
    private const string AzuriteContainerUrl = "http://127.0.0.1:10000/devstoreaccount1/likvido";

    private readonly List<Uri> clientsCreatedFor = new();
    private readonly Mock<BlobClient> blobClient = new();

    public enum Method
    {
        GetAsync,
        GetMetadataAsync,
        DeleteAsync,
        DeleteIfExistsAsync,
        GetBlobSasUriAsync,
    }

    public static TheoryData<Method, string> RefusedUrls()
    {
        var urls = new[]
        {
            "https://otheraccount.blob.core.windows.net/likvido/cases/x.pdf",
            "http://account.blob.core.windows.net/likvido/cases/x.pdf",
            "https://169.254.169.254/likvido/x",
            "https://account.blob.core.windows.net.example.com/likvido/x",
            "https://account.blob.core.windows.net:8443/likvido/x",
            "ftp://account.blob.core.windows.net/likvido/x",
            "https://account.blob.core.windows.net/likvido/",
            "https://account.blob.core.windows.net/likvido",
        };
        var data = new TheoryData<Method, string>();
        foreach (var method in Enum.GetValues<Method>())
        {
            foreach (var url in urls)
            {
                data.Add(method, url);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusedUrls))]
    public async Task UrlMethod_WhenUrlIsNotOnTheOwnEndpoint_ShouldThrowWithoutCreatingAClient(Method method, string url)
    {
        var service = CreateService(OwnContainerUrl);

        await Should.ThrowAsync<ArgumentException>(() => Call(service, method, url));

        clientsCreatedFor.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Method.GetAsync)]
    [InlineData(Method.GetMetadataAsync)]
    [InlineData(Method.DeleteAsync)]
    [InlineData(Method.DeleteIfExistsAsync)]
    [InlineData(Method.GetBlobSasUriAsync)]
    public async Task UrlMethod_WhenHostIsAnotherAccount_ShouldNameTheHostRule(Method method)
    {
        var service = CreateService(OwnContainerUrl);

        var ex = await Should.ThrowAsync<ArgumentException>(() => Call(service, method, "https://otheraccount.blob.core.windows.net/likvido/x.pdf"));

        ex.Message.ShouldContain("is not this storage account's host 'account.blob.core.windows.net'");
    }

    [Theory]
    [InlineData(Method.GetAsync)]
    [InlineData(Method.GetMetadataAsync)]
    [InlineData(Method.DeleteAsync)]
    [InlineData(Method.DeleteIfExistsAsync)]
    [InlineData(Method.GetBlobSasUriAsync)]
    public async Task UrlMethod_WhenSchemeIsHttp_ShouldNameTheSchemeRule(Method method)
    {
        var service = CreateService(OwnContainerUrl);

        var ex = await Should.ThrowAsync<ArgumentException>(() => Call(service, method, "http://account.blob.core.windows.net/likvido/x.pdf"));

        ex.Message.ShouldContain("must use https");
    }

    [Theory]
    [InlineData(Method.GetAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf")]
    [InlineData(Method.GetMetadataAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf")]
    [InlineData(Method.DeleteAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf")]
    [InlineData(Method.DeleteIfExistsAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf")]
    [InlineData(Method.GetBlobSasUriAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf")]
    [InlineData(Method.GetAsync, "https://ACCOUNT.Blob.Core.Windows.Net/Likvido/cases/x.pdf")]
    [InlineData(Method.DeleteAsync, "HTTPS://account.blob.core.windows.net:443/LIKVIDO/cases/x.pdf")]
    [InlineData(Method.GetBlobSasUriAsync, "https://account.blob.core.windows.net/likvido/cases/x.pdf?t=123")]
    [InlineData(Method.GetAsync, "https://account.blob.core.windows.net/lik%76ido/cases/x%20y.pdf")]
    public async Task UrlMethod_WhenUrlIsInTheOwnContainer_ShouldCallStorage(Method method, string url)
    {
        SetupBlobClient();
        var service = CreateService(OwnContainerUrl);

        await Call(service, method, url);

        clientsCreatedFor.ShouldBe([new Uri(url)]);
        VerifyStorageCalled(method);
    }

    [Theory]
    [InlineData(Method.GetAsync)]
    [InlineData(Method.GetMetadataAsync)]
    public async Task ReadMethod_WhenUrlIsInAnotherContainer_ShouldReturnNullWithoutCreatingAClient(Method method)
    {
        var service = CreateService(OwnContainerUrl);

        var result = await Call(service, method, "https://account.blob.core.windows.net/other/likvido/x.pdf");

        result.ShouldBeNull();
        clientsCreatedFor.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Method.DeleteAsync)]
    [InlineData(Method.DeleteIfExistsAsync)]
    [InlineData(Method.GetBlobSasUriAsync)]
    public async Task WriteOrSignMethod_WhenUrlIsInAnotherContainer_ShouldThrowWithoutCreatingAClient(Method method)
    {
        var service = CreateService(OwnContainerUrl);

        var ex = await Should.ThrowAsync<ArgumentException>(() => Call(service, method, "https://account.blob.core.windows.net/other/likvido/x.pdf"));

        ex.Message.ShouldContain("is not in the container 'likvido'");
        clientsCreatedFor.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Method.GetAsync)]
    [InlineData(Method.GetMetadataAsync)]
    [InlineData(Method.DeleteAsync)]
    [InlineData(Method.DeleteIfExistsAsync)]
    [InlineData(Method.GetBlobSasUriAsync)]
    public async Task UrlMethod_OnAzurite_ShouldAcceptTheOwnContainerUnderTheAccountSegment(Method method)
    {
        SetupBlobClient();
        var service = CreateService(AzuriteContainerUrl);

        await Call(service, method, "http://127.0.0.1:10000/devstoreaccount1/likvido/cases/x.pdf");

        clientsCreatedFor.Count.ShouldBe(1);
        VerifyStorageCalled(method);
    }

    [Theory]
    [InlineData("http://127.0.0.1:10000/otheraccount/likvido/x.pdf")]
    [InlineData("http://127.0.0.1:10000/likvido/x.pdf")]
    [InlineData("https://127.0.0.1:10000/devstoreaccount1/likvido/x.pdf")]
    [InlineData("http://127.0.0.1:10001/devstoreaccount1/likvido/x.pdf")]
    [InlineData("http://localhost:10000/devstoreaccount1/likvido/x.pdf")]
    public async Task UrlMethod_OnAzurite_ShouldRefuseAnotherAccountSchemeOrEndpoint(string url)
    {
        var service = CreateService(AzuriteContainerUrl);

        await Should.ThrowAsync<ArgumentException>(() => service.GetAsync(new Uri(url)));

        clientsCreatedFor.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_OnAzurite_WhenUrlIsInAnotherContainer_ShouldReturnNull()
    {
        var service = CreateService(AzuriteContainerUrl);

        var result = await service.GetAsync(new Uri("http://127.0.0.1:10000/devstoreaccount1/other/x.pdf"));

        result.ShouldBeNull();
        clientsCreatedFor.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cases/x.pdf")]
    public async Task GetBlobSasUriAsync_WhenUrlIsNotAbsolute_ShouldThrow(string? url)
    {
        var service = CreateService(OwnContainerUrl);

        await Should.ThrowAsync<ArgumentException>(() => service.GetBlobSasUriAsync(url!));

        clientsCreatedFor.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenUriIsRelative_ShouldThrow()
    {
        var service = CreateService(OwnContainerUrl);

        await Should.ThrowAsync<ArgumentException>(() => service.GetAsync(new Uri("/likvido/x.pdf", UriKind.Relative)));
    }

    // The public constructor builds the container URL from the connection string; refused URLs never reach the network.
    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=account;AccountKey=" + AccountKey + ";EndpointSuffix=core.windows.net", "https://otheraccount.blob.core.windows.net/likvido/x.pdf")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=account;AccountKey=" + AccountKey + ";EndpointSuffix=core.windows.net", "https://169.254.169.254/likvido/x")]
    [InlineData("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=" + AccountKey + ";BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;", "http://127.0.0.1:10000/otheraccount/likvido/x.pdf")]
    public async Task PublicConstructor_ShouldRefuseUrlsOutsideTheConnectionStringsEndpoint(string connectionString, string url)
    {
        var service = new AzureStorageService(new StorageConfiguration { ConnectionString = connectionString }, "likvido");

        await Should.ThrowAsync<ArgumentException>(() => service.DeleteIfExistsAsync(new Uri(url)));
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=account;AccountKey=" + AccountKey + ";EndpointSuffix=core.windows.net", "https://account.blob.core.windows.net/other/x.pdf")]
    [InlineData("DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=" + AccountKey + ";BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;", "http://127.0.0.1:10000/devstoreaccount1/other/x.pdf")]
    public async Task PublicConstructor_WhenUrlIsInAnotherContainerOnTheOwnEndpoint_ShouldReturnNull(string connectionString, string url)
    {
        var service = new AzureStorageService(new StorageConfiguration { ConnectionString = connectionString }, "likvido");

        (await service.GetAsync(new Uri(url))).ShouldBeNull();
    }

    private AzureStorageService CreateService(string containerUrl)
    {
        var containerClient = new Mock<BlobContainerClient>();
        containerClient.SetupGet(c => c.Uri).Returns(new Uri(containerUrl));
        containerClient.SetupGet(c => c.Name).Returns("likvido");

        return new AzureStorageService(containerClient.Object, uri =>
        {
            clientsCreatedFor.Add(uri);
            return blobClient.Object;
        });
    }

    private void SetupBlobClient()
    {
        blobClient
            .Setup(b => b.DownloadToAsync(It.IsAny<Stream>()))
            .ReturnsAsync(Mock.Of<Response>());
        blobClient
            .Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(BlobsModelFactory.BlobProperties(metadata: new Dictionary<string, string>()), Mock.Of<Response>()));
        blobClient
            .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient
            .Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        blobClient
            .Setup(b => b.GenerateSasUri(It.IsAny<BlobSasBuilder>()))
            .Returns(new Uri("https://account.blob.core.windows.net/likvido/cases/x.pdf?sig=abc"));
    }

    private void VerifyStorageCalled(Method method)
    {
        switch (method)
        {
            case Method.GetAsync:
                blobClient.Verify(b => b.DownloadToAsync(It.IsAny<Stream>()), Times.Once);
                break;
            case Method.GetMetadataAsync:
                blobClient.Verify(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
                break;
            case Method.DeleteAsync:
            case Method.DeleteIfExistsAsync:
                blobClient.Verify(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
                break;
            case Method.GetBlobSasUriAsync:
                blobClient.Verify(b => b.GenerateSasUri(It.IsAny<BlobSasBuilder>()), Times.Once);
                break;
        }
    }

    private static async Task<object?> Call(AzureStorageService service, Method method, string url) => method switch
    {
        Method.GetAsync => await service.GetAsync(new Uri(url)),
        Method.GetMetadataAsync => await service.GetMetadataAsync(new Uri(url)),
        Method.DeleteAsync => await DeleteAsync(service, url),
        Method.DeleteIfExistsAsync => await service.DeleteIfExistsAsync(new Uri(url)),
        Method.GetBlobSasUriAsync => await service.GetBlobSasUriAsync(url),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static async Task<object?> DeleteAsync(AzureStorageService service, string url)
    {
        await service.DeleteAsync(new Uri(url));
        return null;
    }
}
