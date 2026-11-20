using BookTracker.Application;
using BookTracker.Application.Publishers;

namespace BookTracker.Web.ViewModels;

// Backs the /publishers page. Lists all Publisher entities with their
// edition counts and supports rename / delete-unused / merge.
//
// Publishers are simpler than Authors — no canonical/alias model — so
// name variants like "Tor" vs "Tor Books" are reconciled via outright
// merge (target absorbs all editions, source deleted). Imprint-style
// aliasing (Pan → Macmillan) is a future extension.
//
// Reads go through GetPublisherList / GetPublisherEditions; writes dispatch
// RenamePublisher / DeleteUnusedPublisher / MergePublishers (PR6b-2). The VM
// keeps only presentation state — the per-row expand set and the lazily-loaded
// edition-detail cache, invalidated whenever a structural change could affect
// what's displayed.
public class PublisherListViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    public bool Loading { get; private set; } = true;
    public IReadOnlyList<PublisherRow> Publishers { get; private set; } = [];
    public string? SuccessMessage { get; set; }

    public HashSet<int> ExpandedPublisherIds { get; private set; } = [];
    public Dictionary<int, PublisherDetail> DetailByPublisherId { get; private set; } = [];

    public async Task LoadAsync()
    {
        Loading = true;
        Publishers = await dispatcher.Query(new GetPublisherList());
        Loading = false;
    }

    public async Task ToggleExpandAsync(int publisherId)
    {
        if (ExpandedPublisherIds.Remove(publisherId)) return;
        ExpandedPublisherIds.Add(publisherId);
        if (!DetailByPublisherId.ContainsKey(publisherId))
        {
            DetailByPublisherId[publisherId] = await dispatcher.Query(new GetPublisherEditions(publisherId));
        }
    }

    public async Task RenameAsync(int publisherId, string newName)
    {
        var result = await DispatchAsync(() => dispatcher.Send(new RenamePublisher(publisherId, newName)));
        await ApplyAsync(result, publisherId);
    }

    public async Task DeleteUnusedAsync(int publisherId)
    {
        var result = await DispatchAsync(() => dispatcher.Send(new DeleteUnusedPublisher(publisherId)));
        await ApplyAsync(result, publisherId);
    }

    public async Task MergeAsync(int sourceId, int targetId)
    {
        var result = await DispatchAsync(() => dispatcher.Send(new MergePublishers(sourceId, targetId)));
        await ApplyAsync(result, sourceId, targetId);
    }

    // A changed result is a success: show its message and drop the stale
    // drill-down cache for the affected rows before reloading the list. A
    // guard refusal (name clash, publisher in use) carries a message but no
    // change and goes to Error. A null result means the dispatch itself was
    // refused and Error is already set.
    private async Task ApplyAsync(PublisherAdminResult? result, params int[] affectedIds)
    {
        SuccessMessage = null;
        if (result is null) return;
        if (result.Changed)
        {
            SuccessMessage = result.Message;
            InvalidateDetailsFor(affectedIds);
            await LoadAsync();
        }
        else if (result.Message is not null)
        {
            Error = result.Message;
        }
    }

    private void InvalidateDetailsFor(params int[] publisherIds)
    {
        foreach (var id in publisherIds) DetailByPublisherId.Remove(id);
    }
}
