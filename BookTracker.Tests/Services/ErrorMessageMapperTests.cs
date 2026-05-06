using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class ErrorMessageMapperTests
{
    [Fact]
    public void Map_DbUpdateException_ReturnsCouldntSaveTitle()
    {
        var msg = ErrorMessageMapper.Map(new DbUpdateException("conflict"));

        Assert.Equal("Couldn't save your change", msg.Title);
        Assert.Contains("logged", msg.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_HttpRequestException_ReturnsExternalServiceTitle()
    {
        var msg = ErrorMessageMapper.Map(new HttpRequestException("timeout"));

        Assert.Equal("Couldn't reach an external service", msg.Title);
    }

    [Fact]
    public void Map_GenericException_ReturnsDefaultTitle()
    {
        // Any unmapped shape falls through to the generic message.
        var msg = ErrorMessageMapper.Map(new InvalidOperationException("oops"));

        Assert.Equal("Something went wrong", msg.Title);
    }

    [Fact]
    public void Map_NullException_ReturnsDefaultTitle()
    {
        // /Error can be hit without a captured exception (e.g. direct
        // navigation, or a re-execute that lost the feature). The mapper
        // must still render a sensible message rather than NRE.
        var msg = ErrorMessageMapper.Map(null);

        Assert.Equal("Something went wrong", msg.Title);
    }
}
