using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class BuildInfoTests
{
    [Theory]
    [InlineData("1.0.0+abcdef0123456789abcdef0123456789abcdef01", "abcdef0123456789abcdef0123456789abcdef01")]
    [InlineData("1.2.3-pre+abc1234", "abc1234")]
    [InlineData("0.0.0+a", "a")]
    public void ParseSha_ExtractsSuffixAfterPlus(string informationalVersion, string expected)
    {
        Assert.Equal(expected, BuildInfo.ParseSha(informationalVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0.0")]               // local-dev shape with no SourceRevisionId
    [InlineData("1.0.0-pre")]
    [InlineData("1.0.0+")]              // dangling plus, no sha — treat as missing
    public void ParseSha_ReturnsNull_WhenNoShaSuffix(string? informationalVersion)
    {
        Assert.Null(BuildInfo.ParseSha(informationalVersion));
    }
}
