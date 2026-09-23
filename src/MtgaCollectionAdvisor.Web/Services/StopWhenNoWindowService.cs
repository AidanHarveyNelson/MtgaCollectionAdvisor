using MtgaCollectionAdvisor.Core.Hosting;

namespace MtgaCollectionAdvisor.Web.Services;

/// <summary>
/// Stops the app once <see cref="WindowPresence"/> says no window has been attached for the
/// grace period. Stopping disposes <see cref="AdvisorSession"/>, which ends the MTG Arena
/// watcher and the Player.log poller, and frees port 5199.
/// </summary>
public sealed class StopWhenNoWindowService(WindowPresence presence, IHostApplicationLifetime lifetime) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (presence.ShouldStop(DateTimeOffset.UtcNow))
            {
                lifetime.StopApplication();
                return;
            }
        }
    }
}
