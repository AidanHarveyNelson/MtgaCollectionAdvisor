using Microsoft.AspNetCore.Components.Server.Circuits;
using MtgaCollectionAdvisor.Core;
using MtgaCollectionAdvisor.Core.Configuration;
using MtgaCollectionAdvisor.Core.Export;
using MtgaCollectionAdvisor.Core.Hosting;
using MtgaCollectionAdvisor.Web.Components;
using MtgaCollectionAdvisor.Web.Services;

const string AppUrl = "http://localhost:5199";
const string InstanceMarker = "MtgaDeckAdvisor";

var openWindow = !args.Contains("--no-browser");

// Launched again while an instance is still running: open a window on that one rather
// than failing to bind the port.
if (await IsAlreadyRunningAsync())
{
    if (openWindow) LaunchUi(AppUrl);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls(AppUrl);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Features:CreatorVideos in appsettings.json (or Features__CreatorVideos in the
// environment). Absent means off, so a release never gains the tab by accident.
builder.Services.AddSingleton(AppConfig.Default with
{
    CreatorVideosEnabled = builder.Configuration.GetValue("Features:CreatorVideos", false)
});
builder.Services.AddSingleton<AdvisorSession>();

// The window is only a browser pointed at this server; these stop the server once it has
// been closed, so nothing keeps watching MTG Arena with no window open.
builder.Services.AddSingleton<WindowPresence>();
builder.Services.AddScoped<CircuitHandler, WindowPresenceCircuitHandler>();
builder.Services.AddHostedService<StopWhenNoWindowService>();

var app = builder.Build();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/instance", () => InstanceMarker);

// Downloads for the Export menu. Plain links with a download attribute, which Blazor
// leaves to the browser rather than routing.
app.MapGet("/export/collection.txt", (AdvisorSession session, CancellationToken ct) =>
    Download(session, async (export, token) => await export.CollectionTextAsync(token), ct));
app.MapGet("/export/collection.json", (AdvisorSession session, CancellationToken ct) =>
    Download(session, async (export, token) => await export.CollectionJsonAsync(token), ct));
app.MapGet("/export/user-decks.zip", (AdvisorSession session, CancellationToken ct) =>
    Download(session, async (export, token) => await export.UserDecksAsync(token), ct));
app.MapGet("/export/arena-decks.zip", (AdvisorSession session, bool? all, CancellationToken ct) =>
    Download(session, (export, token) => export.ArenaDecksAsync(all == true, token), ct));

await app.Services.GetRequiredService<AdvisorSession>().InitializeAsync();

if (openWindow)
{
    _ = Task.Run(() => LaunchUi(AppUrl));
}

app.Run();

static async Task<IResult> Download(
    AdvisorSession session, Func<DataExportService, CancellationToken, Task<ExportFile?>> write, CancellationToken ct)
{
    if (session.DataExport is not { } export) return Results.Problem("The app has not finished starting.");
    if (await write(export, ct) is not { } file) return Results.Problem("Nothing to export yet.", statusCode: 404);
    return Results.File(file.Content, file.ContentType, file.FileName);
}

// Asks the port whether this app is already on it. Anything else there - or nothing -
// is left for the bind to report as usual.
static async Task<bool> IsAlreadyRunningAsync()
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    try
    {
        return await client.GetStringAsync($"{AppUrl}/instance") == InstanceMarker;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return false;
    }
}

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
