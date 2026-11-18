using System.Net;
using System.Text;
using BookTracker.Shared.Online;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class BookLookupServiceTests
{
    private const string Isbn = "9780645840407";

    [Fact]
    public async Task LookupByIsbnAsync_UsesOpenLibraryFirst_AndDoesNotCallDownstreamProviders()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/api/books"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "title": "Open Library Title",
                    "authors": [ { "name": "OL Author" } ]
                  }
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Open Library", result!.Source);
        Assert.Equal("Open Library Title", result.Title);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("googleapis.com"));
        Assert.DoesNotContain(handler.Requests, r => r.Contains("trove.nla.gov.au"));
    }

    [Fact]
    public async Task LookupByIsbnAsync_FallsThroughToTrove_WhenOpenLibraryAndGoogleBooksMiss()
    {
        var handler = new FakeHandler
        {
            // Open Library returns an empty object: no key matching the ISBN.
            ["openlibrary.org/api/books"] = OkJson("{}"),
            // Google Books returns no items.
            ["googleapis.com/books/v1/volumes"] = OkJson("""{ "items": [] }"""),
            ["trove.nla.gov.au"] = OkJson($$"""
                {
                  "category": [
                    {
                      "code": "book",
                      "records": {
                        "total": 1,
                        "work": [
                          {
                            "title": "Trove Title",
                            "contributor": ["Trove Author"],
                            "issued": "2022",
                            "subject": ["Horror", "Fiction"]
                          }
                        ]
                      }
                    }
                  ]
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Trove", result!.Source);
        Assert.Equal("Trove Title", result.Title);
        Assert.Equal("Trove Author", result.Author);
        Assert.Equal(2022, result.DatePrinted?.Year);
        Assert.Contains(result.GenreCandidates, g => g.Contains("Horror", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LookupByIsbnAsync_SkipsTroveSilently_WhenApiKeyMissing()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/api/books"] = OkJson("{}"),
            ["googleapis.com/books/v1/volumes"] = OkJson("""{ "items": [] }""")
        };

        var svc = CreateService(handler, troveKey: "");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("trove.nla.gov.au"));
    }

    [Fact]
    public async Task LookupByIsbnAsync_HandlesSingleValueTroveFields_AsStringsOrArrays()
    {
        // Trove v3 sometimes returns multi-valued fields as a single string
        // instead of a one-element array when there's only one value. The
        // service should treat both shapes identically.
        var handler = new FakeHandler
        {
            ["openlibrary.org/api/books"] = OkJson("{}"),
            ["googleapis.com/books/v1/volumes"] = OkJson("""{ "items": [] }"""),
            ["trove.nla.gov.au"] = OkJson("""
                {
                  "category": [
                    {
                      "code": "book",
                      "records": {
                        "total": 1,
                        "work": [
                          {
                            "title": "Solo Title",
                            "contributor": "Solo Author",
                            "issued": "2019",
                            "subject": "Mystery"
                          }
                        ]
                      }
                    }
                  ]
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Solo Author", result!.Author);
        Assert.Contains(result.GenreCandidates, g => g.Contains("Mystery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LookupByIsbnAsync_ReturnsNull_WhenTroveReturnsNoWork()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/api/books"] = OkJson("{}"),
            ["googleapis.com/books/v1/volumes"] = OkJson("""{ "items": [] }"""),
            ["trove.nla.gov.au"] = OkJson("""
                {
                  "category": [
                    { "code": "book", "records": { "total": 0 } }
                  ]
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupByIsbnAsync_ReturnsNull_ForMalformedIsbn()
    {
        var handler = new FakeHandler();
        var svc = CreateService(handler, troveKey: "present");

        var result = await svc.LookupByIsbnAsync("not-an-isbn", CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LookupByIsbnAsync_PopulatesSeries_FromOpenLibraryDetailsView()
    {
        // Open Library's `jscmd=data` view (the curated abbreviated shape) does
        // NOT include the series field. Series lives only in `jscmd=details`.
        // The service makes two OL calls — data for the friendly fields, then
        // details for series — and combines the result. This test proves both
        // calls happen and the details payload is what populates Series.
        var handler = new FakeHandler
        {
            ["jscmd=data"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "title": "Sourcery",
                    "authors": [ { "name": "Terry Pratchett" } ]
                  }
                }
                """),
            ["jscmd=details"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "details": {
                      "series": [ "Discworld -- 5" ]
                    }
                  }
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sourcery", result!.Title);
        Assert.Equal("Discworld", result.Series);
        Assert.Equal(5, result.SeriesNumber);
        Assert.Equal("5", result.SeriesNumberRaw);
        Assert.Contains(handler.Requests, r => r.Contains("jscmd=details"));
    }

    [Fact]
    public async Task LookupByIsbnAsync_LeavesSeriesNull_WhenDetailsCallFails()
    {
        // Graceful degrade: if the details call fails (404, parse error, etc.),
        // the data view still produces a valid result with Series=null. The
        // suggestion banner falls back to local title/author matching.
        var handler = new FakeHandler
        {
            ["jscmd=data"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "title": "Sourcery",
                    "authors": [ { "name": "Terry Pratchett" } ]
                  }
                }
                """)
            // No mock for jscmd=details — handler returns 404, service swallows.
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sourcery", result!.Title);
        Assert.Null(result.Series);
        Assert.Null(result.SeriesNumber);
        Assert.Null(result.WorkKey);
    }

    [Fact]
    public async Task LookupByIsbnAsync_TakesTheWorkKey_FromTheFirstWorkInTheDetailsView()
    {
        var handler = new FakeHandler
        {
            ["jscmd=data"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": { "title": "Sourcery" }
                }
                """),
            ["jscmd=details"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "details": {
                      "works": [ { "key": "/works/OL45804W" }, { "key": "/works/OL99W" } ]
                    }
                  }
                }
                """)
        };

        var result = await CreateService(handler, troveKey: "").LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("/works/OL45804W", result!.WorkKey);
        Assert.Null(result.Series);
    }

    [Fact]
    public async Task LookupByIsbnAsync_IpLiteralOpenLibraryCovers_FallThroughToTheIsbnCover_AndKeepTheRestOfTheResult()
    {
        var handler = new FakeHandler
        {
            ["jscmd=data"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "title": "Sourcery",
                    "authors": [ { "name": "Terry Pratchett" } ],
                    "publishers": [ { "name": "Corgi" } ],
                    "cover": {
                      "large": "https://169.254.169.254/latest/meta-data/x.jpg",
                      "medium": "https://10.0.0.5/x-M.jpg"
                    }
                  }
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal($"https://covers.openlibrary.org/b/isbn/{Isbn}-L.jpg", result!.CoverUrl);
        Assert.Equal("Open Library", result.Source);
        Assert.Equal("Sourcery", result.Title);
        Assert.Equal("Terry Pratchett", result.Author);
        Assert.Equal("Corgi", result.Publisher);
    }

    [Fact]
    public async Task LookupByIsbnAsync_IpLiteralLargeCover_FallsThroughToTheMediumCover()
    {
        var handler = new FakeHandler
        {
            ["jscmd=data"] = OkJson($$"""
                {
                  "ISBN:{{Isbn}}": {
                    "title": "Sourcery",
                    "cover": {
                      "large": "https://169.254.169.254/latest/meta-data/x.jpg",
                      "medium": "https://covers.openlibrary.org/b/id/1-M.jpg"
                    }
                  }
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "present");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.Equal("https://covers.openlibrary.org/b/id/1-M.jpg", result?.CoverUrl);
    }

    [Fact]
    public void FirstAcceptableCoverUrl_EveryCandidateRefused_ReturnsNull()
    {
        var svc = CreateService(new FakeHandler(), troveKey: "");

        Assert.Null(svc.FirstAcceptableCoverUrl("Open Library", Isbn,
            "https://169.254.169.254/x.jpg", null, "http://covers.openlibrary.org/x.jpg", "  "));
    }

    [Fact]
    public void FirstAcceptableCoverUrl_ReturnsTheFirstAcceptableCandidateTrimmed()
    {
        var svc = CreateService(new FakeHandler(), troveKey: "");

        Assert.Equal("https://covers.openlibrary.org/b/id/2-M.jpg", svc.FirstAcceptableCoverUrl("Open Library", Isbn,
            "https://user:pw@covers.openlibrary.org/x.jpg", "  https://covers.openlibrary.org/b/id/2-M.jpg  ", "https://covers.openlibrary.org/b/id/3-M.jpg"));
    }

    [Fact]
    public async Task LookupByIsbnAsync_UpgradesGoogleBooksHttpCoverToHttps()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/api/books"] = OkJson("{}"),
            ["googleapis.com/books/v1/volumes"] = OkJson("""
                {
                  "items": [
                    {
                      "volumeInfo": {
                        "title": "Google Title",
                        "imageLinks": { "thumbnail": "http://books.google.com/books/content?id=abc&printsec=frontcover&img=1&zoom=1" }
                      }
                    }
                  ]
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "");
        var result = await svc.LookupByIsbnAsync(Isbn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://books.google.com/books/content?id=abc&printsec=frontcover&img=1&zoom=1", result!.CoverUrl);
    }

    [Theory]
    // Plain name, no order.
    [InlineData("Discworld", "Discworld", null, null)]
    // Common Open Library separator patterns.
    [InlineData("Discworld -- 5", "Discworld", 5, "5")]
    [InlineData("Discworld #5", "Discworld", 5, "5")]
    [InlineData("Foundation series ; bk. 1", "Foundation series", 1, "1")]
    [InlineData("The Lord of the Rings ;, pt. 1", "The Lord of the Rings", 1, "1")]
    // Trailing-space-then-number fallback.
    [InlineData("Discworld 5", "Discworld", 5, "5")]
    // Non-integer order — name extracted, integer null, raw preserved
    // (per the revised Q3 default: don't truncate, surface raw for manual use).
    [InlineData("Discworld -- 5.5", "Discworld", null, "5.5")]
    [InlineData("Stormlight Archive #4.5", "Stormlight Archive", null, "4.5")]
    // Whitespace + edge cases.
    [InlineData("  Discworld  ", "Discworld", null, null)]
    public void ParseOpenLibrarySeries_HandlesCommonShapes(string raw, string? expectedName, int? expectedNumber, string? expectedRaw)
    {
        var (name, number, numberRaw) = BookLookupService.ParseOpenLibrarySeries([raw]);

        Assert.Equal(expectedName, name);
        Assert.Equal(expectedNumber, number);
        Assert.Equal(expectedRaw, numberRaw);
    }

    [Fact]
    public void ParseOpenLibrarySeries_ReturnsNull_WhenInputIsMissingOrEmpty()
    {
        Assert.Equal((null, (int?)null, null), BookLookupService.ParseOpenLibrarySeries(null));
        Assert.Equal((null, (int?)null, null), BookLookupService.ParseOpenLibrarySeries([]));
        Assert.Equal((null, (int?)null, null), BookLookupService.ParseOpenLibrarySeries(["", "   "]));
    }

    [Fact]
    public void ParseOpenLibrarySeries_TakesFirstNonEmptyEntry_WhenMultiple()
    {
        // Open Library sometimes returns multiple `series` strings — varies in
        // quality, so we trust the first non-empty entry.
        var (name, _, _) = BookLookupService.ParseOpenLibrarySeries(["Foundation series", "Foundation"]);
        Assert.Equal("Foundation series", name);
    }

    [Fact]
    public async Task GetAuthorWorksAsync_AsksOpenLibraryForTheAuthorPhrase_AndMapsTheDocs()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/search.json"] = OkJson("""
                {
                  "docs": [
                    { "key": "/works/OL2W", "title": "Small Gods", "first_publish_year": 1992, "edition_count": 60 },
                    { "key": "/works/OL1W", "title": "The Colour of Magic", "first_publish_year": 1983, "edition_count": 90, "cover_i": 123 },
                    { "title": "No key, so no work" }
                  ]
                }
                """)
        };

        var svc = CreateService(handler, troveKey: "");
        var works = await svc.GetAuthorWorksAsync("  Martin \"Doc\" Grant ", CancellationToken.None);

        var request = Uri.UnescapeDataString(Assert.Single(handler.Requests));
        Assert.Contains("q=author:\"Martin \\\"Doc\\\" Grant\"", request);
        Assert.Contains("limit=100", request);
        Assert.Contains("fields=key,title,first_publish_year,edition_count,cover_i", request);
        Assert.Contains("sort=editions", request);
        Assert.Equal(
            new[]
            {
                new OnlineAuthorWork("/works/OL1W", "The Colour of Magic", 1983, "https://covers.openlibrary.org/b/id/123-M.jpg", 90, null),
                new OnlineAuthorWork("/works/OL2W", "Small Gods", 1992, null, 60, null),
            },
            works);
    }

    [Fact]
    public async Task GetAuthorWorksAsync_ProviderError_ReturnsEmpty()
    {
        var handler = new FakeHandler { ["openlibrary.org/search.json"] = new HttpResponseMessage(HttpStatusCode.InternalServerError) };

        var works = await CreateService(handler, troveKey: "").GetAuthorWorksAsync("Terry Pratchett", CancellationToken.None);

        Assert.Empty(works);
    }

    [Fact]
    public async Task GetAuthorWorksAsync_TransportException_ReturnsEmpty()
    {
        var handler = new FakeHandler { Throws = new HttpRequestException("connection reset") };

        var works = await CreateService(handler, troveKey: "").GetAuthorWorksAsync("Terry Pratchett", CancellationToken.None);

        Assert.Empty(works);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAuthorWorksAsync_BlankAuthor_MakesNoRequest(string author)
    {
        var handler = new FakeHandler();

        var works = await CreateService(handler, troveKey: "").GetAuthorWorksAsync(author, CancellationToken.None);

        Assert.Empty(works);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetWorkEditionsAsync_AsksForTheWorksEditions_AndMapsTheEntries()
    {
        var handler = new FakeHandler
        {
            ["openlibrary.org/works/OL45804W/editions.json"] = OkJson("""
                {
                  "entries": [
                    {
                      "title": "Mort",
                      "publishers": ["Corgi", "Transworld"],
                      "publish_date": "1988",
                      "physical_format": "Paperback",
                      "isbn_10": ["0-552-13106-x"],
                      "isbn_13": ["978-0-552-13106-3"],
                      "covers": [8231432]
                    },
                    {
                      "title": "Mort",
                      "publishers": ["Gollancz"],
                      "isbn_10": ["057503 9123"],
                      "covers": [-1]
                    },
                    { "title": "Mort (book club)", "publishers": ["Book Club Associates"], "publish_date": "Nov 1987" },
                    { "title": "Mort, no publisher" },
                    { "publishers": ["Nobody"] }
                  ]
                }
                """)
        };

        var editions = await CreateService(handler, troveKey: "").GetWorkEditionsAsync("/works/OL45804W", CancellationToken.None);

        Assert.Equal("https://openlibrary.org/works/OL45804W/editions.json?limit=50", Assert.Single(handler.Requests));
        Assert.Equal(
            new[]
            {
                new OnlineEdition("9780552131063", "Mort", "Corgi", "1988", "Paperback",
                    "https://covers.openlibrary.org/b/id/8231432-M.jpg", null),
                new OnlineEdition("0575039123", "Mort", "Gollancz", null, null,
                    "https://covers.openlibrary.org/b/isbn/0575039123-M.jpg", null),
                new OnlineEdition(null, "Mort (book club)", "Book Club Associates", "Nov 1987", null, null, null),
            },
            editions);
    }

    [Fact]
    public async Task GetWorkEditionsAsync_UpperCasesAnIsbn10CheckDigit()
    {
        var handler = new FakeHandler
        {
            ["editions.json"] = OkJson("""{ "entries": [ { "isbn_10": ["0-552-13106-x"] } ] }""")
        };

        var editions = await CreateService(handler, troveKey: "").GetWorkEditionsAsync("/works/OL1W", CancellationToken.None);

        Assert.Equal("055213106X", Assert.Single(editions).Isbn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("OL45804W")]
    [InlineData("/works/OL45804M")]
    [InlineData("/works/OL45804W\n")]
    [InlineData("/works/OL45804W/../../authors")]
    public async Task GetWorkEditionsAsync_MalformedKey_MakesNoRequest(string workKey)
    {
        var handler = new FakeHandler();

        var editions = await CreateService(handler, troveKey: "").GetWorkEditionsAsync(workKey, CancellationToken.None);

        Assert.Empty(editions);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetWorkEditionsAsync_ProviderError_ReturnsEmpty()
    {
        var handler = new FakeHandler { ["editions.json"] = new HttpResponseMessage(HttpStatusCode.InternalServerError) };

        var editions = await CreateService(handler, troveKey: "").GetWorkEditionsAsync("/works/OL45804W", CancellationToken.None);

        Assert.Empty(editions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetWorkEditionsAsync_TransportException_ReturnsEmpty()
    {
        var handler = new FakeHandler { Throws = new HttpRequestException("connection reset") };

        var editions = await CreateService(handler, troveKey: "").GetWorkEditionsAsync("/works/OL45804W", CancellationToken.None);

        Assert.Empty(editions);
    }

    private static BookLookupService CreateService(FakeHandler handler, string troveKey)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new TroveOptions { ApiKey = troveKey });
        return new BookLookupService(http, NullLogger<BookLookupService>.Instance, options);
    }

    private static HttpResponseMessage OkJson(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _routes = new();
        public List<string> Requests { get; } = [];

        // Set to make every request fail at the transport, as a dropped
        // connection would, rather than with a status code.
        public Exception? Throws { get; init; }

        public HttpResponseMessage this[string urlSubstring]
        {
            set => _routes[urlSubstring] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);
            if (Throws is not null) throw Throws;
            foreach (var (substring, response) in _routes)
            {
                if (url.Contains(substring, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(response);
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
