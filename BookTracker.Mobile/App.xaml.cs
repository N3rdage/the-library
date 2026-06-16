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
        // AppShell hosts the Find / Wishlist / Gaps bottom tabs and injects
        // the tab pages from DI (its ctor takes them). Drill-downs push within
        // the active tab's stack, so the tab bar stays visible.
        var shell = _services.GetRequiredService<AppShell>();
        return new Window(shell);
    }
}
