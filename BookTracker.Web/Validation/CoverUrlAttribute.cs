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

    public override bool IsValid(object? value) =>
        value is null || (value is string url && CoverFetchPolicy.IsAcceptableCoverUrl(url));
}
