using Anthropic.SDK.Constants;
using BookTracker.Application.Ai;

namespace BookTracker.Web.Services;

public enum AIProvider
{
    Anthropic,
    MicrosoftFoundry,
    AzureOpenAI
}

public class AIOptions
{
    public const string SectionName = "AI";

    public AIProvider DefaultProvider { get; set; } = AIProvider.Anthropic;

    public AnthropicOptions Anthropic { get; set; } = new();
    public MicrosoftFoundryOptions MicrosoftFoundry { get; set; } = new();
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();

    /// <summary>US-dollar prices per million tokens, keyed by the model string
    /// the usage rows carry: the model id for Anthropic, the deployment name
    /// for Azure OpenAI and Microsoft Foundry. A model with no entry shows its
    /// tokens on /assistant without a cost.</summary>
    public Dictionary<string, AiModelPrice> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string FastModel { get; set; } = AnthropicModels.Claude46Sonnet;
    public string DeepModel { get; set; } = AnthropicModels.Claude46Opus;
    public int MaxTokens { get; set; } = 1024;
}

public class MicrosoftFoundryOptions
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
