using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

/// <summary>
/// Consolidates common control interactions with the YouTube HTML5 player. Activities can rely on
/// this helper instead of re-implementing selectors, key presses or guard logic for ads.
/// </summary>
internal class PlayerControlHelper
{
    private static readonly string[] SkipButtonSelectors =
    {
        "button.ytp-ad-skip-button-modern",
        ".ytp-ad-skip-button",
        ".ytp-ad-skip-button-container button",
        "button.ytp-skip-ad-button"
    };

    private readonly IPage _page;
    private readonly MouseHelper _mouseHelper;
    private readonly ILogger? _logger;
    private readonly Random _random = RandomProvider.Shared;

    public PlayerControlHelper(IPage page, MouseHelper? mouseHelper = null, ILogger? logger = null)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _mouseHelper = mouseHelper ?? new MouseHelper(page);
        _logger = logger;
    }

    public async Task<bool> EnsurePlayerVisibleAsync()
    {
        var video = _page.Locator("video.html5-main-video");
        try
        {
            return await video.IsVisibleAsync(new() { Timeout = 5000 });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HandleAdsAsync(CancellationToken cancellationToken)
    {
        if (!await IsAdShowingAsync(cancellationToken))
        {
            return false;
        }

        if (await TrySkipAdAsync(cancellationToken))
        {
            return true;
        }

        _logger?.LogDebug("Ad playing without skip option; waiting briefly.");
        await DelayAsync(_random.Next(1200, 2200), cancellationToken);
        return true;
    }

    public async Task<bool> TrySkipAdAsync(CancellationToken cancellationToken)
    {
        foreach (string selector in SkipButtonSelectors)
        {
            var locator = _page.Locator(selector);
            if (!await locator.IsVisibleAsync(new() { Timeout = 100 }))
            {
                continue;
            }

            try
            {
                _logger?.LogInformation("Clicking YouTube skip button '{Selector}'.", selector);
                await _mouseHelper.MoveAndClickAsync(locator);
                await WaitForAdToFinishAsync(cancellationToken);
                return true;
            }
            catch (PlaywrightException ex)
            {
                _logger?.LogDebug(ex, "Failed skipping ad with selector {Selector}.", selector);
            }
        }

        return false;
    }

    public async Task<bool> IsAdShowingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var player = _page.Locator("#movie_player, .html5-video-player, ytd-player");
            if (await player.CountAsync() == 0)
            {
                return false;
            }

            return await player.EvaluateAsync<bool>(
                "player => Boolean(player?.classList?.contains('ad-showing') || player?.classList?.contains('ad-interrupting'))",
                cancellationToken: cancellationToken);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task WaitForAdToFinishAsync(CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(20))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await IsAdShowingAsync(cancellationToken))
            {
                await DelayAsync(_random.Next(300, 700), cancellationToken);
                return;
            }

            await DelayAsync(_random.Next(500, 900), cancellationToken);
        }
    }

    public async Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken)
    {
        return await NavigationPattern.TryPressAsync(_page, "k", logger: _logger, cancellationToken: cancellationToken);
    }

    public async Task<bool> ToggleMuteAsync(CancellationToken cancellationToken)
    {
        return await NavigationPattern.TryPressAsync(_page, "m", probability: 1.0, logger: _logger, cancellationToken: cancellationToken);
    }

    public async Task<bool> AdjustVolumeViaSliderAsync(CancellationToken cancellationToken)
    {
        var slider = _page.Locator(".ytp-volume-slider");
        if (!await slider.IsVisibleAsync(new() { Timeout = 500 }))
        {
            return false;
        }

        var box = await slider.BoundingBoxAsync();
        if (box == null)
        {
            return false;
        }

        double ratio = _random.NextDouble();
        float offsetX = (float)(box.Width * ratio);
        float offsetY = (float)(box.Height / 2);

        try
        {
            await slider.ClickAsync(new() { Position = new() { X = offsetX, Y = offsetY } });
            _logger?.LogTrace("Adjusted volume slider to {Ratio:P0}.", ratio);
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed adjusting YouTube volume slider.");
            return false;
        }
    }

    public async Task<bool> AdjustVolumeViaKeysAsync(CancellationToken cancellationToken)
    {
        string key = _random.NextDouble() < 0.5 ? "ArrowUp" : "ArrowDown";
        return await NavigationPattern.TryPressAsync(_page, key, logger: _logger, cancellationToken: cancellationToken);
    }

    public async Task<bool> SeekRelativeAsync(double percentageDelta, CancellationToken cancellationToken)
    {
        percentageDelta = Math.Clamp(percentageDelta, -0.9, 0.9);

        try
        {
            return await _page.EvaluateAsync<bool>(
                "delta => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video || !isFinite(video.duration) || video.duration <= 0) {\n                        return false;\n                    }\n                    const normalized = Math.min(0.995, Math.max(0.005, (video.currentTime / video.duration) + delta));\n                    const newTime = video.duration * normalized;\n                    if (Math.abs(newTime - video.currentTime) < 0.25) {\n                        return false;\n                    }\n                    video.currentTime = newTime;\n                    return true;\n                }",
                percentageDelta,
                cancellationToken: cancellationToken);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed seeking video by delta {Delta}.", percentageDelta);
            return false;
        }
    }

    public async Task<bool> ToggleFullScreenAsync(CancellationToken cancellationToken)
    {
        return await NavigationPattern.TryPressAsync(_page, "f", logger: _logger, cancellationToken: cancellationToken);
    }

    public async Task<bool> ToggleTheaterModeAsync(CancellationToken cancellationToken)
    {
        return await NavigationPattern.TryPressAsync(_page, "t", logger: _logger, cancellationToken: cancellationToken);
    }

    public async Task<bool> ChangePlaybackSpeedAsync(CancellationToken cancellationToken)
    {
        double[] speeds = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };
        double target = speeds[_random.Next(speeds.Length)];

        try
        {
            return await _page.EvaluateAsync<bool>(
                "speed => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video) {\n                        return false;\n                    }\n                    if (Math.abs(video.playbackRate - speed) < 0.01) {\n                        const alternatives = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];\n                        const different = alternatives.find(s => Math.abs(s - video.playbackRate) > 0.01);\n                        if (different) {\n                            video.playbackRate = different;\n                            return true;\n                        }\n                        return false;\n                    }\n                    video.playbackRate = speed;\n                    return true;\n                }",
                target,
                cancellationToken: cancellationToken);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed setting playback speed to {Speed}.", target);
            return false;
        }
    }

    public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Activity handles cancellation.
        }
    }

    public async Task<bool> EnsureVideoPlayingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _page.EvaluateAsync<bool>(
                "() => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video) return false;\n                    if (video.paused) {\n                        video.play();\n                        return false;\n                    }\n                    return true;\n                }",
                cancellationToken: cancellationToken);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task PerformRandomControlActionAsync(CancellationToken cancellationToken)
    {
        var actions = new List<Func<CancellationToken, Task<bool>>>
        {
            TogglePlayPauseAsync,
            ToggleMuteAsync,
            AdjustVolumeViaSliderAsync,
            AdjustVolumeViaKeysAsync,
            async token => await SeekRelativeAsync(_random.NextDouble() * 0.3 - 0.15, token),
            ToggleFullScreenAsync,
            ToggleTheaterModeAsync,
            ChangePlaybackSpeedAsync
        };

        int attempts = 0;
        while (attempts < actions.Count)
        {
            int index = _random.Next(actions.Count);
            var action = actions[index];
            bool success = await action(cancellationToken);
            if (success)
            {
                return;
            }

            attempts++;
        }
    }
}
