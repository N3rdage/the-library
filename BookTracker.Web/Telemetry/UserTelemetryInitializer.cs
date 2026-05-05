using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace BookTracker.Web.Telemetry;

/// <summary>
/// Stamps the Easy Auth-authenticated user's name onto every telemetry
/// item so traces, requests, and exceptions are filterable per user in
/// App Insights. Single-user app today; the enrichment is forward-cover
/// so when a second user appears their telemetry is already segmentable.
/// </summary>
public class UserTelemetryInitializer(IHttpContextAccessor httpContextAccessor) : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        var name = httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            telemetry.Context.User.AuthenticatedUserId = name;
        }
    }
}
