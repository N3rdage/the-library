namespace BookTracker.Web.Services;

public class AIAssistantOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = "";

    /// <summary>Model for fast/cheap operations (genre suggestions, collection cataloguing).</summary>
    public string FastModel { get; set; } = "claude-sonnet-4-5-20250514";

    /// <summary>Model for deeper analysis (book recommendations, suitability assessment).</summary>
    public string DeepModel { get; set; } = "claude-opus-4-5-20250514";

    public int MaxTokens { get; set; } = 1024;
}
