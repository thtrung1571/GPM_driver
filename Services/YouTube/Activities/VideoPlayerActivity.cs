using System;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Services.YouTube;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

/// <summary>
/// Represents a warmup activity that watches the currently loaded YouTube video while interacting
/// with the player controls in a realistic manner.
/// </summary>
internal class VideoPlayerActivity
{
    private readonly IPage _page;
    private readonly PlayerControlHelper _controls;
    private readonly ILogger<VideoPlayerActivity>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    public int MinDelayBetweenActionsMs { get; set; } = 2500;
    public int MaxDelayBetweenActionsMs { get; set; } = 6000;

    public VideoPlayerActivity(IPage page, ILogger<VideoPlayerActivity>? logger = null, PlayerControlHelper? controlHelper = null)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _logger = logger;
        _controls = controlHelper ?? new PlayerControlHelper(page, logger: logger);
    }

    public async Task WatchCurrentVideoAsync(
        TimeSpan watchDuration,
        CancellationToken cancellationToken = default)
    {
        if (watchDuration <= TimeSpan.Zero)
        {
            return;
        }

        _logger?.LogInformation("Watching current YouTube video for {Duration}.", watchDuration);

        if (!await _controls.WaitForPlayerReadyAsync(cancellationToken))
        {
            _logger?.LogWarning("Timed out waiting for YouTube player to become ready.");
            return;
        }

        bool playerReady = await _controls.EnsurePlayerVisibleAsync();
        if (!playerReady)
        {
            _logger?.LogWarning("Unable to locate visible YouTube player. Skipping watch routine.");
            return;
        }

        var stopAt = DateTime.UtcNow + watchDuration;
        bool firstLoop = true;

        while (DateTime.UtcNow < stopAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _controls.HandleAdsAsync(cancellationToken))
            {
                // Ads consume time but we resume the loop immediately afterwards.
                continue;
            }

            if (firstLoop)
            {
                await _controls.EnsureVideoPlayingAsync(cancellationToken);
                firstLoop = false;
            }

            await _controls.PerformRandomControlActionAsync(cancellationToken);

            int delay = _random.Next(MinDelayBetweenActionsMs, MaxDelayBetweenActionsMs + 1);
            await _controls.DelayAsync(delay, cancellationToken);
        }

        _logger?.LogInformation("Completed watch routine on {Url}.", _page.Url);
    }
}
