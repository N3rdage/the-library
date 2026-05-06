using BookTracker.Web;

var app = ProgramSetup.Build(args);
await ProgramSetup.RunMigrationsAsync(app);
await app.RunAsync();

// Surfaces the implicit top-level Program class so test code can
// reference it (e.g. Playwright fixture's reflection-friendly entry).
// No behaviour change.
public partial class Program;
