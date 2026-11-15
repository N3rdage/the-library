using System.Net;
using BookTracker.Web.Services.Covers;

namespace BookTracker.Tests.Services.Covers;

[Trait("Category", TestCategories.Unit)]
public class CoverFetchPolicyTests
{
    [Theory]
    [InlineData("https://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("https://books.google.com/books/content?id=abc&printsec=frontcover&img=1&zoom=1")]
    [InlineData("https://covers.openlibrary.org:443/b/isbn/x-L.jpg")]
    public void Validate_PublicHttpsUrl_IsAllowed(string url)
    {
        Assert.Null(CoverFetchPolicy.Validate(url));
    }

    [Theory]
    [InlineData("http://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("ftp://covers.openlibrary.org/x.jpg")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("https://169.254.169.254/metadata")]
    [InlineData("https://user:pw@host/x")]
    [InlineData("https://host:8443/x")]
    [InlineData("covers/x.jpg")]
    [InlineData("/covers/x.jpg")]
    [InlineData("")]
    [InlineData("not a url at all")]
    public void Validate_RefusedUrl_ReturnsReason(string url)
    {
        Assert.False(string.IsNullOrWhiteSpace(CoverFetchPolicy.Validate(url)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("  https://covers.openlibrary.org/b/isbn/x-L.jpg  ")]
    public void IsAcceptableCoverUrl_BlankOrFetchableHttps_IsTrue(string? url)
    {
        Assert.True(CoverFetchPolicy.IsAcceptableCoverUrl(url));
    }

    [Theory]
    [InlineData("http://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("ftp://covers.openlibrary.org/x.jpg")]
    [InlineData("https://93.184.216.34/x.jpg")]
    [InlineData("https://[2606:2800:220:1:248:1893:25c8:1946]/x.jpg")]
    [InlineData("https://user:pw@covers.openlibrary.org/x.jpg")]
    [InlineData("https://covers.openlibrary.org:8443/x.jpg")]
    [InlineData("covers/x.jpg")]
    [InlineData("/covers/x.jpg")]
    public void IsAcceptableCoverUrl_UnfetchableUrl_IsFalse(string url)
    {
        Assert.False(CoverFetchPolicy.IsAcceptableCoverUrl(url));
    }

    [Fact]
    public void ForLogging_UrlWithUserInfo_StripsIt()
    {
        var logged = CoverFetchPolicy.ForLogging("https://user:secret@covers.example.org/b/x.jpg?v=1");

        Assert.Equal("https://covers.example.org/b/x.jpg?v=1", logged);
        Assert.DoesNotContain("secret", logged);
        Assert.DoesNotContain("user", logged);
    }

    [Theory]
    [InlineData("https://covers.openlibrary.org/b/isbn/x-L.jpg")]
    [InlineData("http://cdn.example.org:8080/x.png")]
    public void ForLogging_UrlWithoutUserInfo_IsUnchanged(string url)
    {
        Assert.Equal(url, CoverFetchPolicy.ForLogging(url));
    }

    [Theory]
    [InlineData("not a url at all")]
    [InlineData("covers/x.jpg")]
    [InlineData("")]
    public void ForLogging_UnparsableInput_IsReturnedAsIs(string input)
    {
        Assert.Equal(input, CoverFetchPolicy.ForLogging(input));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.9")]
    [InlineData("0.0.0.0")]
    [InlineData("0.12.34.56")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void IsPublicAddress_BlockedAddress_IsFalse(string address)
    {
        Assert.False(CoverFetchPolicy.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("::ffff:93.184.216.34")]
    public void IsPublicAddress_PublicAddress_IsTrue(string address)
    {
        Assert.True(CoverFetchPolicy.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void FirstAllowedAddress_AllBlocked_ReturnsNull()
    {
        IPAddress[] resolved = [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("169.254.169.254"), IPAddress.IPv6Loopback];

        Assert.Null(CoverFetchPolicy.FirstAllowedAddress(resolved));
    }

    [Fact]
    public void FirstAllowedAddress_MixedAddresses_ReturnsFirstPublic()
    {
        IPAddress[] resolved =
        [
            IPAddress.Parse("192.168.0.10"),
            IPAddress.Parse("93.184.216.34"),
            IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946"),
        ];

        Assert.Equal(IPAddress.Parse("93.184.216.34"), CoverFetchPolicy.FirstAllowedAddress(resolved));
    }

    [Fact]
    public void CreateHandler_DisablesRedirectsAndInstallsConnectCallback()
    {
        using var handler = CoverFetchPolicy.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.NotNull(handler.ConnectCallback);
    }
}
