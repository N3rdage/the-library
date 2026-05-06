using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookTracker.Web.Services;

/// <summary>
/// Manages AI provider selection and creates the active service instance.
/// Scoped lifetime — one per Blazor circuit. Allows runtime provider switching.
/// </summary>
public class AIProviderFactory(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IOptions<AIOptions> options,
    ILoggerFactory loggerFactory)
{
    private readonly AIOptions _options = options.Value;
    private AIProvider _activeProvider;
    private IAIAssistantService? _currentService;

    public AIProvider ActiveProvider => _activeProvider;
    public IReadOnlyList<AIProvider> AvailableProviders => GetAvailableProviders();

    public void Initialize()
    {
        _activeProvider = _options.DefaultProvider;
    }

    public IAIAssistantService GetService()
    {
        if (_currentService is null || GetProviderForService(_currentService) != _activeProvider)
        {
            _currentService = CreateService(_activeProvider);
        }
        return _currentService;
    }

    public void SwitchProvider(AIProvider provider)
    {
        if (_activeProvider == provider) return;
        _activeProvider = provider;
        _currentService = null; // force recreation on next GetService()
    }

    private IAIAssistantService CreateService(AIProvider provider) => provider switch
    {
        AIProvider.Anthropic => new AnthropicAIAssistantService(dbFactory, _options.Anthropic, loggerFactory.CreateLogger<AnthropicAIAssistantService>()),
        AIProvider.MicrosoftFoundry => new MicrosoftFoundryAIAssistantService(dbFactory, _options.MicrosoftFoundry),
        AIProvider.AzureOpenAI => new AzureOpenAIAssistantService(dbFactory, _options.AzureOpenAI),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private List<AIProvider> GetAvailableProviders()
    {
        var providers = new List<AIProvider>();
        if (!string.IsNullOrEmpty(_options.Anthropic.ApiKey))
            providers.Add(AIProvider.Anthropic);
        if (!string.IsNullOrEmpty(_options.MicrosoftFoundry.ApiKey) && !string.IsNullOrEmpty(_options.MicrosoftFoundry.Endpoint))
            providers.Add(AIProvider.MicrosoftFoundry);
        if (!string.IsNullOrEmpty(_options.AzureOpenAI.ApiKey) && !string.IsNullOrEmpty(_options.AzureOpenAI.Endpoint))
            providers.Add(AIProvider.AzureOpenAI);
        return providers;
    }

    private static AIProvider? GetProviderForService(IAIAssistantService service) => service switch
    {
        AnthropicAIAssistantService => AIProvider.Anthropic,
        MicrosoftFoundryAIAssistantService => AIProvider.MicrosoftFoundry,
        AzureOpenAIAssistantService => AIProvider.AzureOpenAI,
        _ => null
    };
}
