using BookTracker.Application;
using BookTracker.Application.Authors;

namespace BookTracker.Web.ViewModels;

// Backs /authors/{id}. Owns rename + alias-management ops and the
// per-author drill-down (Works / Books). Reads go through GetAuthorDetail;
// writes dispatch RenameAuthor / MarkAuthorAsAliasOf / PromoteAuthorToCanonical
// (PR6b-2). The VM keeps only presentation state — the Works/Books toggle,
// the loading/not-found flags, and the success/error toast strings.
public class AuthorDetailViewModel(IDispatcher dispatcher)
{
    public bool Loading { get; private set; } = true;
    public bool NotFound { get; private set; }
    public AuthorHeader? Header { get; private set; }
    public AuthorDetail Detail { get; private set; } = AuthorDetail.Empty;
    public AuthorViewMode ViewMode { get; set; } = AuthorViewMode.Works;

    /// <summary>List of canonical authors used by the "mark as alias of…" dropdown.</summary>
    public IReadOnlyList<CanonicalCandidate> CanonicalCandidates { get; private set; } = [];

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

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

        var result = await dispatcher.Send(new RenameAuthor(Header.Id, newName));
        Apply(result);
        if (result.Success) await LoadAsync(Header.Id);
    }

    public async Task MarkAsAliasOfAsync(int canonicalId)
    {
        if (Header is null || Header.Id == canonicalId) return;

        var result = await dispatcher.Send(new MarkAuthorAsAliasOf(Header.Id, canonicalId));
        Apply(result);
        if (result.Success) await LoadAsync(Header.Id);
    }

    public async Task PromoteToCanonicalAsync()
    {
        if (Header is null || Header.CanonicalAuthorId is null) return;

        var result = await dispatcher.Send(new PromoteAuthorToCanonical(Header.Id));
        Apply(result);
        if (result.Success) await LoadAsync(Header.Id);
    }

    private void Apply(AuthorAdminResult result)
    {
        // Each admin action owns exactly one channel — clear the opposite one so a
        // stale alert from a prior action can't sit next to the new one (both render
        // simultaneously in Detail.razor).
        SuccessMessage = result.SuccessMessage;
        ErrorMessage = result.ErrorMessage;
    }

    public enum AuthorViewMode { Works, Books }
}
