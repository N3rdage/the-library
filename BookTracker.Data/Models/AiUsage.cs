using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// One row per successful AI call: which provider and model answered, which
// operation asked, and the token counts the provider reported. The cost
// summary on /assistant is computed from these rows and the prices in
// configuration, so a price change re-prices the history.
//
// No relation to any aggregate; bookkeeping like CoverMirrorState and
// MaintenanceLog. Not part of the Book aggregate, so a usage row never bumps
// Book.UpdatedAt.
public class AiUsage
{
    public int Id { get; set; }

    public DateTime OccurredAt { get; set; }

    // The AIProvider enum name as the provider passes it: Anthropic,
    // MicrosoftFoundry or AzureOpenAI. Kept a string so the table reads
    // without the enum and survives a rename.
    [Required, MaxLength(40)]
    public string Provider { get; set; } = string.Empty;

    // The model id for Anthropic; the deployment name for Azure OpenAI and
    // Microsoft Foundry. Prices are keyed by this value.
    [Required, MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    // The IAIAssistantService method, without the Async suffix: SuggestGenres,
    // SuggestCollections, SuggestShoppingList, AssessBook, ExtractIsbn.
    [Required, MaxLength(60)]
    public string Operation { get; set; } = string.Empty;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }
}
