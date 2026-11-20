using BookTracker.Application;
using BookTracker.Application.Authors;

namespace BookTracker.Web.ViewModels;

// Backs /authors/{id}. Owns rename + alias-management ops and the
// per-author drill-down (Works / Books). Reads go through GetAuthorDetail;
// writes dispatch RenameAuthor / MarkAuthorAsAliasOf / PromoteAuthorToCanonical
// (PR6b-2). The VM keeps only presentation state — the Works/Books toggle,
// the loading/not-found flags, and the success message.
public class AuthorDetailViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    public bool Loading { get; private set; } = true;
    public bool NotFound { get; private set; }
    public AuthorHeader? Header { get; private set; }
    public AuthorDetail Detail { get; private set; } = AuthorDetail.Empty;
    public AuthorViewMode ViewMode { get; set; } = AuthorViewMode.Works;

    /// <summary>List of canonical authors used by the "mark as alias of…" dropdown.</summary>
    public IReadOnlyList<CanonicalCandidate> CanonicalCandidates { get; private set; } = [];

    public string? SuccessMessage { get; set; }

    public async Task LoadAsync(int authorId)
    {
        Loading = true;
        NotFound = false;
        Header = null;
        Detail = AuthorDetail.Empty;

        var result = await dispatcher.Query(new GetAuthorDetail(authorId));
        if (result is null)
        {
            NotFound = true;
            Loading = false;
            return;
        }

        Header = result.Header;
        Detail = result.Detail;
        CanonicalCandidates = result.CanonicalCandidates;
        Loading = false;
    }

    public async Task RenameAsync(string newName)
    {
        if (Header is null) return;

        var result = await DispatchAsync(() => dispatcher.Send(new RenameAuthor(Header.Id, newName)));
        if (Apply(result)) await LoadAsync(Header.Id);
    }

    public async Task MarkAsAliasOfAsync(int canonicalId)
    {
        if (Header is null || Header.Id == canonicalId) return;

        var result = await DispatchAsync(() => dispatcher.Send(new MarkAuthorAsAliasOf(Header.Id, canonicalId)));
        if (Apply(result)) await LoadAsync(Header.Id);
    }

    public async Task PromoteToCanonicalAsync()
    {
        if (Header is null || Header.CanonicalAuthorId is null) return;

        var result = await DispatchAsync(() => dispatcher.Send(new PromoteAuthorToCanonical(Header.Id)));
        if (Apply(result)) await LoadAsync(Header.Id);
    }

    // Each admin action owns exactly one channel (the success message or Error), so
    // a stale alert from a prior action can't sit next to the new one. A null
    // result means the domain refused the dispatch and Error is already set.
    private bool Apply(AuthorAdminResult? result)
    {
        SuccessMessage = result?.SuccessMessage;
        if (result?.ErrorMessage is { } message) Error = message;
        return result is { Success: true };
    }

    public enum AuthorViewMode { Works, Books }
}
