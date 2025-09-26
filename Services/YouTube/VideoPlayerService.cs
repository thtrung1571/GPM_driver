using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class VideoPlayerService
{
    private readonly Func<Task<IPage>> _ensurePage;
    private readonly Func<MouseHelper?> _mouseProvider;
    private readonly Func<KeyboardHelper?> _keyboardProvider;
    private readonly Random _random;
    private readonly ILogger? _logger;
    private readonly Func<YouTubeWarmupService.IdentityPlaybackPreferences?> _playbackPreferencesAccessor;
    private readonly Action<VideoWatchResult> _recordWatch;
    private readonly Func<Task> _pauseBetweenActions;

    public VideoPlayerService(
        Func<Task<IPage>> ensurePage,
        Func<MouseHelper?> mouseProvider,
        Func<KeyboardHelper?> keyboardProvider,
        Random random,
        ILogger? logger,
        Func<YouTubeWarmupService.IdentityPlaybackPreferences?> playbackPreferencesAccessor,
        Action<VideoWatchResult> recordWatch,
        Func<Task> pauseBetweenActions)
    {
        _ensurePage = ensurePage;
        _mouseProvider = mouseProvider;
        _keyboardProvider = keyboardProvider;
        _random = random;
        _logger = logger;
        _playbackPreferencesAccessor = playbackPreferencesAccessor;
        _recordWatch = recordWatch;
        _pauseBetweenActions = pauseBetweenActions;
    }

    public async Task<VideoWatchResult?> WatchCurrentEntryAsync(
        YouTubeWarmupSettings warmup,
        string context,
        string method,
        string contextDetail,
        string entryPoint,
        string? keyword,
        int? position,
        VideoWatchResult? parent)
    {
        var page = await _ensurePage();
        var kind = YouTubeUrlHelper.GetVideoKind(page.Url);

        return kind == YouTubeUrlHelper.YouTubeVideoKind.Short
            ? await WatchShortAsync(
                warmup,
                context,
                method,
                contextDetail,
                entryPoint,
                position,
                parent)
            : await WatchVideoAsync(
                warmup,
                context,
                method,
                contextDetail,
                entryPoint,
                keyword,
                position,
                parent,
                kind);
    }

    public async Task<VideoWatchResult?> WatchVideoAsync(
        YouTubeWarmupSettings warmup,
        string context,
        string method,
        string contextDetail,
        string entryPoint,
        string? keyword,
        int? position,
        VideoWatchResult? parent,
        YouTubeUrlHelper.YouTubeVideoKind detectedKind)
    {
        var page = await _ensurePage();
        try
        {
            await WaitForStandardPlayerAsync(page);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Video element did not appear for context {Context}.", context);
        }

        await FocusPlayerAsync(page, detectedKind);
        await MaybeSkipAdsAsync(page);

        int? volumePercent = await MaybeAdjustVolumeAsync(page);
        int planned = PlanWatchDuration(warmup.MinWatchMilliseconds, warmup.MaxWatchMilliseconds);
        int actualTarget = ApplyWatchDurationJitter(planned, warmup.MinWatchMilliseconds, warmup.MaxWatchMilliseconds);
        var start = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        await MaybeJitterMouseAsync(page);
        await MaybeInteractWithPlayerUiAsync(page);

        await page.WaitForTimeoutAsync(actualTarget);

        if (_random.NextDouble() < 0.3)
        {
            await page.Keyboard.PressAsync("Space");
            await Task.Delay(_random.Next(800, 1500));
            await page.Keyboard.PressAsync("Space");
        }

        if (_random.NextDouble() < 0.4)
        {
            await ScrollHelper.ScrollRandomAsync(page, _random.Next(1, 4));
        }

        stopwatch.Stop();

        var result = new VideoWatchResult
        {
            Context = context,
            ContextDetail = contextDetail,
            EntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? context : entryPoint,
            Method = method,
            ResultPosition = position,
            Keyword = keyword,
            Url = page.Url,
            Title = await TryGetInnerTextAsync(page.Locator("h1 yt-formatted-string")),
            ChannelName = await TryGetInnerTextAsync(page.Locator("#channel-name a, #owner-name a")),
            PlannedWatchDurationMs = planned,
            ActualWatchDurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
            StartedAt = start,
            IsShort = false,
            ParentVideoUrl = parent?.Url,
            ParentVideoTitle = parent?.Title,
            ParentContext = parent?.ContextDetail ?? parent?.Context,
            VolumePercent = volumePercent
        };

        result.VideoType = YouTubeUrlHelper.GetVideoKind(result.Url);
        result.IsShort = result.VideoType == YouTubeUrlHelper.YouTubeVideoKind.Short;

        _recordWatch(result);
        await _pauseBetweenActions();
        return result;
    }

    public async Task<VideoWatchResult?> WatchShortAsync(
        YouTubeWarmupSettings warmup,
        string context,
        string method,
        string contextDetail,
        string entryPoint,
        int? position,
        VideoWatchResult? parent)
    {
        var page = await _ensurePage();
        await WaitForShortPlayerAsync(page);
        await FocusPlayerAsync(page, YouTubeUrlHelper.YouTubeVideoKind.Short);
        await MaybeSkipAdsAsync(page);

        int? volumePercent = await MaybeAdjustVolumeAsync(page);
        int planned = PlanWatchDuration(warmup.MinShortWatchMilliseconds, warmup.MaxShortWatchMilliseconds);
        int actualTarget = ApplyWatchDurationJitter(planned, warmup.MinShortWatchMilliseconds, warmup.MaxShortWatchMilliseconds);
        var start = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        await MaybeJitterMouseAsync(page);

        await page.WaitForTimeoutAsync(actualTarget);

        if (_random.NextDouble() < 0.25)
        {
            await page.Keyboard.PressAsync("ArrowUp");
            await page.Keyboard.PressAsync("ArrowDown");
        }

        stopwatch.Stop();

        var result = new VideoWatchResult
        {
            Context = context,
            ContextDetail = contextDetail,
            EntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? context : entryPoint,
            Method = method,
            ResultPosition = position,
            Keyword = null,
            Url = page.Url,
            Title = await TryGetInnerTextAsync(page.Locator("h1 yt-formatted-string")),
            ChannelName = await TryGetInnerTextAsync(page.Locator("#channel-name a, #owner-name a")),
            PlannedWatchDurationMs = planned,
            ActualWatchDurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
            StartedAt = start,
            IsShort = true,
            ParentVideoUrl = parent?.Url,
            ParentVideoTitle = parent?.Title,
            ParentContext = parent?.ContextDetail ?? parent?.Context,
            VolumePercent = volumePercent,
            VideoType = YouTubeUrlHelper.YouTubeVideoKind.Short
        };

        _recordWatch(result);
        await _pauseBetweenActions();
        return result;
    }

    private async Task MaybeSkipAdsAsync(IPage page)
    {
        var adOverlay = page.Locator(".ytp-ad-player-overlay, .ytp-ad-overlay-slot, .ad-showing");
        try
        {
            if (await adOverlay.CountAsync() == 0)
            {
                return;
            }

            int dwellMs = await DetermineAdDwellAsync(page);
            if (dwellMs > 0)
            {
                await page.WaitForTimeoutAsync(dwellMs);
            }

            string[] skipSelectors =
            {
                "button.ytp-ad-skip-button", "button.ytp-ad-skip-button-modern", "div.ytp-ad-skip-button-slot button",
                "yt-button-renderer#skip-button button", "button[aria-label*='Skip']"
            };

            for (int attempt = 0; attempt < 6; attempt++)
            {
                bool stillShowing = await page.EvaluateAsync<bool>(
                    "() => document.querySelector('.ad-showing, .ytp-ad-player-overlay, .ytp-ad-overlay-slot') !== null");
                if (!stillShowing)
                {
                    return;
                }

                foreach (string selector in skipSelectors)
                {
                    var skipButton = page.Locator(selector);
                    if (await skipButton.CountAsync() == 0)
                    {
                        continue;
                    }

                    var target = skipButton.First;
                    if (!await target.IsVisibleAsync())
                    {
                        continue;
                    }

                    _logger?.LogDebug("Attempting to skip YouTube ad using selector {Selector}.", selector);
                    var mouse = _mouseProvider();
                    if (mouse != null)
                    {
                        await mouse.MoveAndClickAsync(target);
                    }
                    else
                    {
                        await target.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                    }

                    await Task.Delay(_random.Next(600, 1200));
                    return;
                }

                await Task.Delay(_random.Next(500, 900));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed while attempting to skip YouTube ads.");
        }
    }

    private async Task<int> DetermineAdDwellAsync(IPage page)
    {
        try
        {
            string? durationText = await page.EvaluateAsync<string?>(
                "() => { const el = document.querySelector('.ytp-time-duration, .ytp-ad-duration-remaining'); return el ? el.textContent : null; }");
            int? durationMs = ParseDurationToMilliseconds(durationText);
            if (!durationMs.HasValue)
            {
                return _random.Next(4000, 6500);
            }

            if (durationMs.Value <= 15000)
            {
                return _random.Next(4200, 6000);
            }

            if (durationMs.Value <= 30000)
            {
                return _random.Next(5000, 9000);
            }

            return _random.Next(6500, 12000);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to determine ad dwell time.");
            return _random.Next(4000, 6500);
        }
    }

    private static int? ParseDurationToMilliseconds(string? durationText)
    {
        if (string.IsNullOrWhiteSpace(durationText))
        {
            return null;
        }

        durationText = durationText.Trim();
        var parts = durationText.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        int seconds = 0;
        int multiplier = 1;

        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!int.TryParse(parts[i], out int value))
            {
                return null;
            }

            seconds += value * multiplier;
            multiplier *= 60;
        }

        return seconds * 1000;
    }

    private async Task<int?> MaybeAdjustVolumeAsync(IPage page)
    {
        var playback = _playbackPreferencesAccessor();
        if (playback == null)
        {
            return null;
        }

        playback.MinVolumePercent = Math.Clamp(playback.MinVolumePercent, 0, 100);
        playback.MaxVolumePercent = Math.Clamp(playback.MaxVolumePercent, 0, 100);
        if (playback.MinVolumePercent > playback.MaxVolumePercent)
        {
            (playback.MinVolumePercent, playback.MaxVolumePercent) = (playback.MaxVolumePercent, playback.MinVolumePercent);
        }

        playback.VolumeAdjustmentChance = Math.Clamp(playback.VolumeAdjustmentChance, 0, 1);
        if (playback.VolumeAdjustmentChance <= 0)
        {
            playback.VolumeAdjustmentChance = 0.6;
        }

        if (_random.NextDouble() > playback.VolumeAdjustmentChance)
        {
            return playback.LastVolumePercent;
        }

        double? currentVolume = null;
        try
        {
            currentVolume = await page.EvaluateAsync<double?>("() => { const video = document.querySelector('video'); return video ? video.volume : null; }");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to read current YouTube volume.");
        }

        int min = playback.MinVolumePercent;
        int max = playback.MaxVolumePercent;
        int targetPercent = min == max ? min : _random.Next(min, max + 1);
        double targetVolume = Math.Clamp(targetPercent / 100.0, 0, 1);

        if (currentVolume.HasValue && Math.Abs(currentVolume.Value - targetVolume) < 0.03 && _random.NextDouble() < 0.4)
        {
            int currentPercent = Math.Clamp((int)Math.Round(currentVolume.Value * 100), 0, 100);
            playback.LastVolumePercent = currentPercent;
            return currentPercent;
        }

        var videoLocator = page.Locator("video.html5-main-video, ytd-player video");
        try
        {
            if (await videoLocator.CountAsync() > 0)
            {
                var mouse = _mouseProvider();
                if (mouse != null)
                {
                    await mouse.MoveAndClickAsync(videoLocator.First);
                }
                else
                {
                    await videoLocator.First.ClickAsync();
                }

                await Task.Delay(_random.Next(120, 260));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to focus video element before adjusting volume.");
        }

        bool usedKeyboard = _random.NextDouble() < 0.65;
        if (usedKeyboard)
        {
            double startingVolume = currentVolume ?? (playback.LastVolumePercent.HasValue ? playback.LastVolumePercent.Value / 100.0 : 0.5);
            double step = 0.05;
            int steps = (int)Math.Round((targetVolume - startingVolume) / step);
            string key = steps >= 0 ? "ArrowUp" : "ArrowDown";

            for (int i = 0; i < Math.Abs(steps); i++)
            {
                await page.Keyboard.PressAsync(key);
                await Task.Delay(_random.Next(70, 150));
            }
        }
        else
        {
            try
            {
                await page.EvaluateAsync("(vol) => { const video = document.querySelector('video'); if (video) { video.volume = Math.min(1, Math.max(0, vol)); } }", targetVolume);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to set YouTube volume via script.");
            }
        }

        double? finalVolume = null;
        try
        {
            finalVolume = await page.EvaluateAsync<double?>("() => { const video = document.querySelector('video'); return video ? video.volume : null; }");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to read final YouTube volume.");
        }

        int? finalPercent = finalVolume.HasValue
            ? (int?)Math.Clamp((int)Math.Round(finalVolume.Value * 100), 0, 100)
            : targetPercent;

        playback.LastVolumePercent = finalPercent;
        return finalPercent;
    }

    private async Task WaitForStandardPlayerAsync(IPage page)
    {
        await page.WaitForSelectorAsync("video.html5-main-video, ytd-player video", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 15000
        });
    }

    private async Task WaitForShortPlayerAsync(IPage page)
    {
        await page.WaitForSelectorAsync("ytd-reel-video-renderer, #shorts-player", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 15000
        });
    }

    private async Task FocusPlayerAsync(IPage page, YouTubeUrlHelper.YouTubeVideoKind kind)
    {
        try
        {
            if (kind == YouTubeUrlHelper.YouTubeVideoKind.Short)
            {
                await page.Keyboard.PressAsync("k");
                await Task.Delay(_random.Next(400, 900));
                return;
            }

            var player = page.Locator("video.html5-main-video, ytd-player video");
            if (await player.CountAsync() > 0)
            {
                var mouse = _mouseProvider();
                if (mouse != null)
                {
                    await mouse.MoveAndClickAsync(player.First);
                }
                else
                {
                    await player.First.ClickAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to focus player for kind {Kind}.", kind);
        }
    }

    private async Task MaybeJitterMouseAsync(IPage page)
    {
        if (_random.NextDouble() > 0.45)
        {
            return;
        }

        try
        {
            var mouse = _mouseProvider();
            if (mouse == null)
            {
                return;
            }

            var viewport = await page.EvaluateAsync<int[]>("() => [window.innerWidth || 1280, window.innerHeight || 720]");
            int width = viewport.Length > 0 ? viewport[0] : 1280;
            int height = viewport.Length > 1 ? viewport[1] : 720;
            int centerX = width / 2;
            int seekBarY = height - _random.Next(160, 220);

            await mouse.MoveAsync(centerX + _random.Next(-40, 40), seekBarY);
            await Task.Delay(_random.Next(300, 700));
            await mouse.MoveAsync(centerX + _random.Next(-20, 20), seekBarY - _random.Next(20, 50));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to perform mouse jitter on player.");
        }
    }

    private async Task MaybeInteractWithPlayerUiAsync(IPage page)
    {
        if (_random.NextDouble() > 0.35)
        {
            return;
        }

        try
        {
            double roll = _random.NextDouble();
            var mouse = _mouseProvider();

            if (roll < 0.33)
            {
                var transcriptButton = page.Locator("button[aria-label*='Transcript'], button[aria-label*='Bản phiên âm']");
                if (await transcriptButton.CountAsync() > 0 && await transcriptButton.First.IsVisibleAsync())
                {
                    if (mouse != null)
                    {
                        await mouse.MoveAndClickAsync(transcriptButton.First);
                    }
                    else
                    {
                        await transcriptButton.First.ClickAsync();
                    }

                    await Task.Delay(_random.Next(1500, 2800));
                }
            }
            else if (roll < 0.66)
            {
                var likeButton = page.Locator("ytd-toggle-button-renderer#like-button button");
                if (await likeButton.CountAsync() > 0)
                {
                    if (mouse != null)
                    {
                        await mouse.MoveAsync(_random.Next(200, 320), _random.Next(180, 260));
                        await Task.Delay(_random.Next(200, 450));
                        await mouse.MoveAndClickAsync(likeButton.First);
                    }
                    else
                    {
                        await likeButton.First.ClickAsync();
                    }

                    await Task.Delay(_random.Next(1200, 2000));
                }
            }
            else
            {
                var avatar = page.Locator("#owner #avatar, #channel-name a");
                if (await avatar.CountAsync() > 0)
                {
                    if (mouse != null)
                    {
                        await mouse.MoveAndHoverAsync(avatar.First);
                    }
                    else
                    {
                        await avatar.First.HoverAsync();
                    }

                    await Task.Delay(_random.Next(1000, 2000));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to trigger player UI interaction.");
        }
    }

    private async Task<string?> TryGetInnerTextAsync(ILocator locator)
    {
        try
        {
            if (await locator.CountAsync() == 0)
            {
                return null;
            }

            return (await locator.First.InnerTextAsync())?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private int PlanWatchDuration(int min, int max)
    {
        if (min >= max)
        {
            return min;
        }

        double roll = _random.NextDouble();
        int range = max - min;
        int shortUpper = min + (int)(range * 0.2);
        int mediumUpper = min + (int)(range * 0.7);

        if (roll < 0.15)
        {
            return _random.Next(min, shortUpper + 1);
        }

        if (roll < 0.85)
        {
            return _random.Next(shortUpper, mediumUpper + 1);
        }

        return _random.Next(mediumUpper, max + 1);
    }

    private int ApplyWatchDurationJitter(int planned, int min, int max)
    {
        int jitter = (int)(planned * _random.NextDouble() * 0.2);
        int adjusted = planned + (_random.NextDouble() < 0.5 ? -jitter : jitter);
        return Math.Clamp(adjusted, min, max);
    }
}
