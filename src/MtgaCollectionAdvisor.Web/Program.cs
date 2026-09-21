using MtgaCollectionAdvisor.Core;
using MtgaCollectionAdvisor.Core.Configuration;
using MtgaCollectionAdvisor.Web.Components;
using MtgaCollectionAdvisor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://localhost:5199");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(AppConfig.Default);
builder.Services.AddSingleton<AdvisorSession>();

var app = builder.Build();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.Services.GetRequiredService<AdvisorSession>().InitializeAsync();

if (!args.Contains("--no-browser"))
{
    _ = Task.Run(() => LaunchUi("http://localhost:5199"));
}

app.Run();

// Chromium's --app mode gives a plain window with no address bar or tabs, so the tool
// feels like a desktop app. Falls back to the default browser when neither is present.
static void LaunchUi(string url)
{
    foreach (var browser in (string[])["msedge", "chrome"])
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(browser)
            {
                UseShellExecute = true,
                Arguments = $"--app={url} --window-size=1500,950"
            });
            if (process is not null) return;
        }
        catch
        {
            // Browser not installed - try the next one.
        }
    }

    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
