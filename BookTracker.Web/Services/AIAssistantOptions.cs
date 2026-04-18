namespace BookTracker.Web.Services;

public enum AIProvider
{
    Anthropic,
    AzureFoundry,
    AzureOpenAI
}

public class AIOptions
{
    public const string SectionName = "AI";

    public AIProvider DefaultProvider { get; set; } = AIProvider.Anthropic;

    public AnthropicOptions Anthropic { get; set; } = new();
    public AzureFoundryOptions AzureFoundry { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
}

public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string FastModel { get; set; } = "claude-sonnet-4-20250514";
    public string DeepModel { get; set; } = "claude-opus-4-20250514";
    public int MaxTokens { get; set; } = 1024;
}

public class AzureFoundryOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string FastDeployment { get; set; } = "";
    public string DeepDeployment { get; set; } = "";
    public int MaxTokens { get; set; } = 1024;
}

public class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Deployment { get; set; } = "";
    public int MaxTokens { get; set; } = 1024;
}
