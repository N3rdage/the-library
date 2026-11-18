using Azure.Identity;
using BookTracker.Web.Services.Covers;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverBlobServiceClientFactoryTests
{
    private const string ConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=keyaccount;AccountKey=a2V5;EndpointSuffix=core.windows.net";
    private const string ServiceUri = "https://identityaccount.blob.core.windows.net/";

    [Fact]
    public void ConnectionStringOnly_UsesTheAccountKey()
    {
        var options = new CoverStorageOptions { ConnectionString = ConnectionString };

        Assert.Equal(CoverStorageAuthMode.ConnectionString, options.AuthMode);
        Assert.True(options.IsEnabled);
        var client = CoverBlobServiceClientFactory.Create(options);
        Assert.Equal("keyaccount", client.AccountName);
        Assert.True(client.CanGenerateAccountSasUri, "a key-backed client can sign SAS URIs");
    }

    [Fact]
    public void ServiceUriOnly_UsesTheManagedIdentity()
    {
        var options = new CoverStorageOptions { ServiceUri = ServiceUri };

        Assert.Equal(CoverStorageAuthMode.ManagedIdentity, options.AuthMode);
        Assert.True(options.IsEnabled);
        var client = CoverBlobServiceClientFactory.Create(options);
        Assert.Equal(new Uri(ServiceUri), client.Uri);
        Assert.Equal("identityaccount", client.AccountName);
        Assert.False(client.CanGenerateAccountSasUri, "a token-credential client holds no account key");
    }

    [Fact]
    public void ManagedIdentityCredential_IsDefaultAzureCredential()
    {
        Assert.IsType<DefaultAzureCredential>(CoverBlobServiceClientFactory.CreateCredential());
    }

    [Fact]
    public void BothConfigured_ConnectionStringWins()
    {
        var options = new CoverStorageOptions { ConnectionString = ConnectionString, ServiceUri = ServiceUri };

        Assert.Equal(CoverStorageAuthMode.ConnectionString, options.AuthMode);
        var client = CoverBlobServiceClientFactory.Create(options);
        Assert.Equal("keyaccount", client.AccountName);
        Assert.True(client.CanGenerateAccountSasUri);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", " ")]
    public void NeitherConfigured_IsDisabled(string connectionString, string serviceUri)
    {
        var options = new CoverStorageOptions { ConnectionString = connectionString, ServiceUri = serviceUri };

        Assert.Equal(CoverStorageAuthMode.None, options.AuthMode);
        Assert.False(options.IsEnabled);
        Assert.Throws<InvalidOperationException>(() => CoverBlobServiceClientFactory.Create(options));
    }

    [Fact]
    public void ServiceUriOnly_StorageIsEnabledAndOwnsContainerUrls()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CoverStorageOptions
        {
            ServiceUri = ServiceUri,
            ContainerName = "book-covers",
        });
        using var http = new HttpClient();
        var storage = new BlobBookCoverStorage(options, http,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BlobBookCoverStorage>.Instance);

        Assert.True(storage.IsEnabled);
        Assert.True(storage.IsManagedUrl(ServiceUri + "book-covers/editions/1.jpg?v=1"));
        Assert.False(storage.IsManagedUrl("https://covers.openlibrary.org/b/id/1-L.jpg"));
    }
}
