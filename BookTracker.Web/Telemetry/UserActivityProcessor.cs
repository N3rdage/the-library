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
/// The user is read from the <c>X-MS-CLIENT-PRINCIPAL-NAME</c> header that
/// App Service Easy Auth injects (the app registers no authentication
/// handler, so Easy Auth never populates <c>HttpContext.User</c>), falling
/// back to <c>User.Identity.Name</c> when a handler did populate it.
/// Server (request) Activities that lack <c>server.address</c> get it, and
/// <c>url.scheme</c>, from the request so the exporter can build a URL with a host.
/// The stamp happens in <see cref="OnEnd"/> so it sees the finished request,
/// including its host.
/// </summary>
public class UserActivityProcessor(IHttpContextAccessor httpContextAccessor) : BaseProcessor<Activity>
{
    // The Azure Monitor exporter's ActivityTagsProcessor reads this key into
    // the ai.user.authUserId context tag (user_AuthenticatedId in Kusto).
    public const string EndUserIdTag = "enduser.id";
    public const string PrincipalNameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";
    public const string ServerAddressTag = "server.address";
    public const string UrlSchemeTag = "url.scheme";

    public override void OnEnd(Activity data)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return;
        }

        var name = context.Request.Headers[PrincipalNameHeader].ToString();
        if (string.IsNullOrEmpty(name))
        {
            name = context.User?.Identity?.Name;
        }

        if (!string.IsNullOrEmpty(name))
        {
            data.SetTag(EndUserIdTag, name);
        }

        if (data.Kind == ActivityKind.Server
            && string.IsNullOrEmpty(data.GetTagItem(ServerAddressTag) as string)
            && context.Request.Host.HasValue)
        {
            data.SetTag(ServerAddressTag, context.Request.Host.Host);
            if (string.IsNullOrEmpty(data.GetTagItem(UrlSchemeTag) as string))
            {
                data.SetTag(UrlSchemeTag, context.Request.Scheme);
            }
        }
    }
}
