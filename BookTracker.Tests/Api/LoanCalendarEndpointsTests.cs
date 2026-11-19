using BookTracker.Application;
using BookTracker.Application.Loans;
using BookTracker.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace BookTracker.Tests.Api;

// The response shapes of the .ics endpoints. The .ics text itself is covered
// by LoanCalendarTests and the GetLoanCalendar query against the Testcontainer
// by GetDueBackHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class LoanCalendarEndpointsTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero));
    private readonly DefaultHttpContext _http = new();

    private static LoanCalendarEntry Entry(int id, string borrower = "Sam") =>
        new(id, $"Book {id}", borrower, new DateOnly(2026, 12, 3));

    private void StubCalendar(params LoanCalendarEntry[] entries) =>
        _dispatcher.Query(Arg.Any<GetLoanCalendar>(), Arg.Any<CancellationToken>()).Returns(entries);

    private Task<IResult> ForLoan(int loanId) =>
        LoanCalendarEndpoints.HandleLoanAsync(loanId, _http, _dispatcher, _clock, CancellationToken.None);

    private Task<IResult> ForBorrower(string? borrower) =>
        LoanCalendarEndpoints.HandleBorrowerAsync(borrower, _http, _dispatcher, _clock, CancellationToken.None);

    private string? ContentDisposition => _http.Response.Headers.ContentDisposition;

    [Fact]
    public async Task Loan_returnsTheCalendarAsAnAttachment()
    {
        _http.Request.Host = new HostString("books.example.test", 8443);
        StubCalendar(Entry(7));

        var result = await ForLoan(7);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("text/calendar; charset=utf-8", content.ContentType);
        Assert.Contains("UID:loan-7@books.example.test\r\n", content.ResponseContent);
        Assert.Contains("DTSTAMP:20261119T010000Z\r\n", content.ResponseContent);
        Assert.Equal("attachment; filename=\"loan-7.ics\"", ContentDisposition);
        await _dispatcher.Received(1).Query(new GetLoanCalendar(7, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Loan_withoutAHostHeader_usesTheFallbackHostInTheUid()
    {
        StubCalendar(Entry(7));

        var result = await ForLoan(7);

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Contains($"UID:loan-7@{LoanCalendarEndpoints.FallbackHost}\r\n", content.ResponseContent);
    }

    [Fact]
    public async Task Loan_withNoMatchingEntry_isNotFound_andSetsNoAttachment()
    {
        StubCalendar();

        var result = await ForLoan(404);

        Assert.IsType<NotFound>(result);
        Assert.True(string.IsNullOrEmpty(ContentDisposition));
    }

    [Fact]
    public async Task Borrower_returnsOneEventPerLoan_namedAfterTheBorrower()
    {
        StubCalendar(Entry(1, "Zoë O'Brien"), Entry(2, "Zoë O'Brien"));

        var result = await ForBorrower("  Zoë O'Brien ");

        var content = Assert.IsType<ContentHttpResult>(result);
        Assert.Equal("text/calendar; charset=utf-8", content.ContentType);
        Assert.Contains("UID:loan-1@", content.ResponseContent);
        Assert.Contains("UID:loan-2@", content.ResponseContent);
        Assert.Equal("attachment; filename=\"loans-zo-o-brien.ics\"", ContentDisposition);
        await _dispatcher.Received(1).Query(new GetLoanCalendar(null, "Zoë O'Brien"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Borrower_withNoDatedOpenLoan_isNotFound()
    {
        StubCalendar();

        var result = await ForBorrower("Nobody");

        Assert.IsType<NotFound>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankBorrower_isNotFound_andNothingIsQueried(string? borrower)
    {
        var result = await ForBorrower(borrower);

        Assert.IsType<NotFound>(result);
        await _dispatcher.DidNotReceive().Query(Arg.Any<GetLoanCalendar>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Sam", "sam")]
    [InlineData("Mary-Jane Watson", "mary-jane-watson")]
    [InlineData("  A, B & C!  ", "a-b-c")]
    [InlineData("日本", "borrower")]
    [InlineData("\"quoted\"", "quoted")]
    public void Slug_isAsciiSafe(string name, string expected)
    {
        Assert.Equal(expected, LoanCalendarEndpoints.Slug(name));
    }
}
