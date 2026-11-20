using System.Diagnostics;
using OpenTelemetry;

namespace BookTracker.Web.Telemetry;

/// <summary>
/// Stamps the Easy Auth-authenticated user's name onto every Activity
/// (request, dependency, ...) as the OpenTelemetry <c>enduser.id</c> tag,
/// which the Azure Monitor exporter maps to Application Insights'
/// authenticated user id, so traces, requests, and exceptions are
/// filterable per user. Single-user app today; the enrichment is
/// forward-cover so when a second user appears their telemetry is already
/// segmentable.
/// The stamp happens in <see cref="OnEnd"/>: the server Activity for a
/// request starts before authentication runs, so the user is only known
/// by the time it ends.
/// </summary>
public class UserActivityProcessor(IHttpContextAccessor httpContextAccessor) : BaseProcessor<Activity>
{
    public const string EndUserIdTag = "enduser.id";

    public override void OnEnd(Activity data)
    {
        var name = httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            data.SetTag(EndUserIdTag, name);
        }
    }
}
