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
    internal enum PlayerControlAction
    {
        TogglePlayPause,
        ToggleMute,
        AdjustVolumeSlider,
        AdjustVolumeKeys,
        SeekRelative,
        ToggleFullScreen,
        ToggleTheaterMode,
        ChangePlaybackSpeed
    }

    private static readonly string[] SkipButtonSelectors =
    {
        "button.ytp-ad-skip-button-modern",
        ".ytp-ad-skip-button",
        ".ytp-ad-skip-button-container button",
        "button.ytp-skip-ad-button",
        ".ytp-skip-ad-button"
    };

    private static readonly IReadOnlyDictionary<PlayerControlAction, double> DefaultWeights =
        new Dictionary<PlayerControlAction, double>
        {
            [PlayerControlAction.TogglePlayPause] = 1.0,
            [PlayerControlAction.SeekRelative] = 0.85,
            [PlayerControlAction.ToggleMute] = 0.65,
            [PlayerControlAction.AdjustVolumeKeys] = 0.55,
            [PlayerControlAction.ChangePlaybackSpeed] = 0.45,
            [PlayerControlAction.ToggleTheaterMode] = 0.35,
            [PlayerControlAction.AdjustVolumeSlider] = 0.30,
            [PlayerControlAction.ToggleFullScreen] = 0.25
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

    public static IReadOnlyDictionary<PlayerControlAction, double> DefaultActionWeights => DefaultWeights;

    public async Task<bool> EnsurePlayerVisibleAsync()
    {
        try
        {
            var standardPlayer = _page.Locator("#movie_player");
            if (await standardPlayer.IsVisibleAsync(new() { Timeout = 5000 }))
            {
                return true;
            }

            var shortsPlayer = _page.Locator("#reel-video-renderer");
            if (await shortsPlayer.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return true;
            }

            var video = _page.Locator("video.html5-main-video");
            return await video.IsVisibleAsync(new() { Timeout = 2000 });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> WaitForPlayerReadyAsync(CancellationToken cancellationToken, int timeoutMilliseconds = 15000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, timeoutMilliseconds));

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await EnsurePlayerVisibleAsync())
            {
                try
                {
                    bool ready = await _page.EvaluateAsync<bool>(
                        @"() => {
                            const player = document.querySelector('#movie_player, .html5-video-player, ytd-player');
                            const video = document.querySelector('video.html5-main-video')
                                || document.querySelector('#shorts-player video')
                                || document.querySelector('#reel-video-renderer video');
                            if (!player || !video) {
                                return false;
                            }
                            if (player.classList?.contains('ad-showing') || player.classList?.contains('ad-interrupting')) {
                                return false;
                            }
                            if (video.readyState >= 2 && !video.paused) {
                                return true;
                            }
                            if (video.readyState >= 2) {
                                try { video.play?.(); } catch (e) { }
                            }
                            return false;
                        }");

                    if (ready)
                    {
                        return true;
                    }
                }
                catch (PlaywrightException ex)
                {
                    _logger?.LogTrace(ex, "Player readiness check failed.");
                }
            }

            await DelayAsync(_random.Next(250, 450), cancellationToken);
        }

        _logger?.LogDebug("Timed out waiting for YouTube player to become ready.");
        return false;
    }

    public async Task<bool> HandleAdsAsync(CancellationToken cancellationToken)
    {
        if (await TrySkipAdAsync(cancellationToken))
        {
            return true;
        }

        if (!await IsAdShowingAsync(cancellationToken) && !await IsAdOverlayBlockingControlsAsync(cancellationToken))
        {
            return false;
        }

        if (await TrySkipAdAsync(cancellationToken))
        {
            return true;
        }

        _logger?.LogDebug("Ad playing without skip option; waiting briefly.");
        await WaitForAdToFinishAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TrySkipAdAsync(CancellationToken cancellationToken)
    {
        foreach (string selector in SkipButtonSelectors)
        {
            var locator = _page.Locator(selector);
            if (!await locator.IsVisibleAsync(new() { Timeout = 200 }))
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

            cancellationToken.ThrowIfCancellationRequested();

            return await player.EvaluateAsync<bool>(
                "player => Boolean(player?.classList?.contains('ad-showing') || player?.classList?.contains('ad-interrupting'))");
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<bool> IsAdOverlayBlockingControlsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var overlay = _page.Locator("div.video-ads.ytp-ad-module");
            return await overlay.IsVisibleAsync(new() { Timeout = 100 });
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task WaitForAdToFinishAsync(CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool adShowing = await IsAdShowingAsync(cancellationToken);
            bool overlayBlocking = await IsAdOverlayBlockingControlsAsync(cancellationToken);

            if (!adShowing && !overlayBlocking)
            {
                await DelayAsync(_random.Next(300, 700), cancellationToken);
                return;
            }

            await DelayAsync(_random.Next(600, 900), cancellationToken);
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
            await _mouseHelper.MoveToAsync(slider);
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
            cancellationToken.ThrowIfCancellationRequested();

            return await _page.EvaluateAsync<bool>(
                "delta => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video || !isFinite(video.duration) || video.duration <= 0) {\n                        return false;\n                    }\n                    const normalized = Math.min(0.995, Math.max(0.005, (video.currentTime / video.duration) + delta));\n                    const newTime = video.duration * normalized;\n                    if (Math.abs(newTime - video.currentTime) < 0.25) {\n                        return false;\n                    }\n                    video.currentTime = newTime;\n                    return true;\n                }",
                percentageDelta);
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
            cancellationToken.ThrowIfCancellationRequested();

            return await _page.EvaluateAsync<bool>(
                "speed => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video) {\n                        return false;\n                    }\n                    if (Math.abs(video.playbackRate - speed) < 0.01) {\n                        const alternatives = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];\n                        const different = alternatives.find(s => Math.abs(s - video.playbackRate) > 0.01);\n                        if (different) {\n                            video.playbackRate = different;\n                            return true;\n                        }\n                        return false;\n                    }\n                    video.playbackRate = speed;\n                    return true;\n                }",
                target);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed setting playback speed to {Speed}.", target);
            return false;
        }
    }

    public async Task<bool> IsFullScreenActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await _page.EvaluateAsync<bool>(
                "() => {\n                    const player = document.querySelector('#movie_player, .html5-video-player, ytd-player');\n                    if (document.fullscreenElement) {\n                        return true;\n                    }\n                    return Boolean(player?.classList?.contains('ytp-fullscreen'));\n                }");
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task ExitFullScreenAsync(CancellationToken cancellationToken)
    {
        if (!await IsFullScreenActiveAsync(cancellationToken))
        {
            return;
        }

        bool toggled = await NavigationPattern.TryPressAsync(_page, "f", logger: _logger, cancellationToken: cancellationToken);
        await DelayAsync(_random.Next(200, 400), cancellationToken);

        if (!toggled || await IsFullScreenActiveAsync(cancellationToken))
        {
            await NavigationPattern.TryPressAsync(_page, "Escape", logger: _logger, cancellationToken: cancellationToken);
            await DelayAsync(_random.Next(200, 400), cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();

            return await _page.EvaluateAsync<bool>(
                "() => {\n                    const video = document.querySelector('video.html5-main-video');\n                    if (!video) return false;\n                    if (video.paused) {\n                        video.play();\n                        return false;\n                    }\n                    return true;\n                }");
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<bool> TryPerformActionAsync(PlayerControlAction action, CancellationToken cancellationToken)
    {
        if (await IsAdOverlayBlockingControlsAsync(cancellationToken))
        {
            return false;
        }

        switch (action)
        {
            case PlayerControlAction.TogglePlayPause:
                return await TogglePlayPauseAsync(cancellationToken);
            case PlayerControlAction.ToggleMute:
                return await ToggleMuteAsync(cancellationToken);
            case PlayerControlAction.AdjustVolumeSlider:
                return await AdjustVolumeViaSliderAsync(cancellationToken);
            case PlayerControlAction.AdjustVolumeKeys:
                return await AdjustVolumeViaKeysAsync(cancellationToken);
            case PlayerControlAction.SeekRelative:
                double delta = _random.NextDouble() * 0.25 - 0.125;
                return await SeekRelativeAsync(delta, cancellationToken);
            case PlayerControlAction.ToggleFullScreen:
                return await ToggleFullScreenAsync(cancellationToken);
            case PlayerControlAction.ToggleTheaterMode:
                return await ToggleTheaterModeAsync(cancellationToken);
            case PlayerControlAction.ChangePlaybackSpeed:
                return await ChangePlaybackSpeedAsync(cancellationToken);
            default:
                return false;
        }
    }
}
