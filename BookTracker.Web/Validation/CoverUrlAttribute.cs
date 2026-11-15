using System.ComponentModel.DataAnnotations;
using BookTracker.Web.Services.Covers;

namespace BookTracker.Web.Validation;

/// <summary>
/// Accepts a blank value or a cover URL the cover mirror can fetch
/// (<see cref="CoverFetchPolicy.IsAcceptableCoverUrl"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CoverUrlAttribute : ValidationAttribute
{
    public const string Message = "Use an https:// address with a host name (no IP address, user name or port).";

    public CoverUrlAttribute() : base(Message)
    {
    }

    /// <summary>
    /// <see cref="Message"/> when <paramref name="url"/> is not an acceptable cover
    /// URL, otherwise null; the shape of a MudBlazor field <c>Validation</c> func.
    /// </summary>
    public static string? ErrorFor(string? url) =>
        CoverFetchPolicy.IsAcceptableCoverUrl(url) ? null : Message;

    public override bool IsValid(object? value) =>
        value is null || (value is string url && CoverFetchPolicy.IsAcceptableCoverUrl(url));
}
