using System.Net;
using System.Text;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookTracker.Tests.Services;

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

        public HttpResponseMessage this[string urlSubstring]
        {
            set => _routes[urlSubstring] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);
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
