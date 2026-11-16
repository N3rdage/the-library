namespace BookTracker.Web.Services.Covers;

/// <summary>
/// Outcome of <see cref="IBookCoverStorage.MirrorFromUrlAsync"/>.
/// <see cref="Url"/> is the blob URL when <see cref="Status"/> is
/// <see cref="CoverMirrorStatus.Mirrored"/>, otherwise the source URL unchanged.
/// </summary>
public sealed record CoverMirrorResult(string Url, CoverMirrorStatus Status);

public enum CoverMirrorStatus
{
    /// <summary>The cover was fetched, normalised and stored; Url is the blob URL.</summary>
    Mirrored,

    /// <summary>Nothing to do: the URL is already a blob URL, is blank, or storage is disabled.</summary>
    AlreadyManaged,

    /// <summary>The URL or its content is refused and retrying the same URL cannot succeed.</summary>
    PermanentRefusal,

    /// <summary>The attempt failed for a reason that may clear on a later try.</summary>
    TransientFailure,
}
