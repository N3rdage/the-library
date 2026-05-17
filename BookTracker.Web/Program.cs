using BookTracker.Web;

var app = ProgramSetup.Build(args);
// Migrations apply at deploy-time (CI bundle) in Staging/Production —
// see .github/workflows/deploy.yml + swap.yml and TODO #21. Local
// dev keeps migrate-on-startup so `dotnet watch` Just Works without
// a separate `dotnet ef database update` step.
if (app.Environment.IsDevelopment())
{
    await ProgramSetup.RunMigrationsAsync(app);
}
await app.RunAsync();

// Surfaces the implicit top-level Program class so test code can
// reference it (e.g. Playwright fixture's reflection-friendly entry).
// No behaviour change.
public partial class Program;
