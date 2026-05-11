namespace BookTracker.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolve MainPage from DI so it gets IAuthService + IApiClient.
        // Shell would have tried to construct MainPage parameterless
        // via its ContentTemplate, which would have failed. PR 3 is
        // one page; PR 4+ can swap in Shell or TabbedPage when
        // there are tabs to navigate.
        var page = _services.GetRequiredService<MainPage>();
        return new Window(new NavigationPage(page));
    }
}
