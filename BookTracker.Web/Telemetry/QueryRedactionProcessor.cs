using System.Diagnostics;
using System.Text;
using OpenTelemetry;

namespace BookTracker.Web.Telemetry;

/// <summary>
/// Replaces query-string parameter values on every Activity with
/// <c>Redacted</c>, keeping the parameter names so the telemetry still shows
/// the request's shape (<c>?borrower=Redacted</c>). Query strings carry a
/// third party's name (the loan calendar's <c>borrower</c>), search text, and
/// outgoing API keys (<c>key=</c> on lookup calls), none of which belong in
/// Application Insights. Parameters named in <see cref="AllowedKeys"/> keep
/// their value because it is harmless and useful for diagnosis.
/// Request Activities carry the query in <c>url.query</c>; dependency
/// Activities carry it inside <c>url.full</c>; the older <c>http.url</c> and
/// <c>http.target</c> conventions are covered too. The redaction runs in
/// <see cref="OnEnd"/> so it sees the finished Activity.
/// </summary>
public class QueryRedactionProcessor : BaseProcessor<Activity>
{
    public const string Redacted = "Redacted";

    private static readonly string[] UrlTags = ["url.query", "url.full", "http.url", "http.target"];

    // since is the catalog-snapshot watermark, a timestamp.
    private static readonly HashSet<string> AllowedKeys = new(["since"], StringComparer.OrdinalIgnoreCase);

    public override void OnEnd(Activity data)
    {
        foreach (var tag in UrlTags)
        {
            if (data.GetTagItem(tag) is string value && value.Contains('?'))
            {
                data.SetTag(tag, RedactQuery(value));
            }
        }
    }

    // Works on a bare query ("?a=b") and on a full URL or request target
    // ("https://host/path?a=b"): everything before the first '?' is kept.
    private static string RedactQuery(string value)
    {
        var start = value.IndexOf('?') + 1;
        var result = new StringBuilder(value, 0, start, value.Length);

        foreach (var parameter in value[start..].Split('&'))
        {
            if (result.Length > start)
            {
                result.Append('&');
            }

            var equals = parameter.IndexOf('=');
            if (equals < 0)
            {
                result.Append(parameter);
                continue;
            }

            var key = parameter[..equals];
            result.Append(key).Append('=');
            result.Append(AllowedKeys.Contains(key) ? parameter[(equals + 1)..] : Redacted);
        }

        return result.ToString();
    }
}
