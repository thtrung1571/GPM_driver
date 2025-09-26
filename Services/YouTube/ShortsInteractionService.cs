using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class ShortsInteractionService
{
    private readonly Func<Task<IPage>> _ensurePage;
    private readonly Func<YouTubeWarmupSettings, Task> _ensureOnYouTube;
    private readonly Func<string> _getBaseDomain;
    private readonly Func<IPage, string, Task> _waitForNavigationAsync;
    private readonly VideoPlayerService _videoPlayer;
    private readonly Random _random;
    private readonly Func<MouseHelper?> _mouseProvider;
    private readonly ILogger? _logger;

    public ShortsInteractionService(
        Func<Task<IPage>> ensurePage,
        Func<YouTubeWarmupSettings, Task> ensureOnYouTube,
        Func<string> getBaseDomain,
        Func<IPage, string, Task> waitForNavigationAsync,
        VideoPlayerService videoPlayer,
        Random random,
        Func<MouseHelper?> mouseProvider,
        ILogger? logger)
    {
        _ensurePage = ensurePage;
        _ensureOnYouTube = ensureOnYouTube;
        _getBaseDomain = getBaseDomain;
        _waitForNavigationAsync = waitForNavigationAsync;
        _videoPlayer = videoPlayer;
        _random = random;
        _mouseProvider = mouseProvider;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(YouTubeWarmupSettings warmup, bool preferGuideMenu)
    {
        var page = await _ensurePage();
        await _ensureOnYouTube(warmup);

        bool guideOpened = false;
        if (preferGuideMenu)
        {
            guideOpened = await TryEnsureGuideMenuVisibleAsync(page);
        }

        var shortsLink = await TryFindShortsLinkAsync(page);
        if (shortsLink == null || !await shortsLink.IsVisibleAsync())
        {
            if (!guideOpened)
            {
                guideOpened = await TryEnsureGuideMenuVisibleAsync(page);
            }

            shortsLink = await TryFindShortsLinkAsync(page);
        }

        if (shortsLink == null || !await shortsLink.IsVisibleAsync())
        {
            if (_random.NextDouble() < 0.15)
            {
                await page.GotoAsync($"{_getBaseDomain().TrimEnd('/')}/shorts", new PageGotoOptions
                {
                    Timeout = 45000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                await Task.Delay(_random.Next(800, 1500));
                return await WatchShortsSequenceAsync(warmup);
            }

            return false;
        }

        var mouse = _mouseProvider();
        if (mouse != null)
        {
            await mouse.MoveAndClickAsync(shortsLink);
        }
        else
        {
            await shortsLink.ClickAsync();
        }

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(_random.Next(1000, 2000));

        return await WatchShortsSequenceAsync(warmup);
    }

    private async Task<bool> WatchShortsSequenceAsync(YouTubeWarmupSettings warmup)
    {
        var page = await _ensurePage();
        var shortsTiles = page.Locator("ytd-reel-video-renderer a#thumbnail, ytd-reel-item-renderer a#thumbnail");
        int count = await shortsTiles.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("No shorts available after navigating to Shorts page.");
            return false;
        }

        int index = _random.Next(0, count);
        var target = shortsTiles.Nth(index);
        string beforeClickUrl = page.Url;
        var mouse = _mouseProvider();
        if (mouse != null)
        {
            await mouse.MoveAndClickAsync(target);
        }
        else
        {
            await target.ClickAsync();
        }

        await _waitForNavigationAsync(page, beforeClickUrl);

        int minSequence = Math.Max(1, warmup.MinShortSequenceLength);
        int maxSequence = Math.Max(minSequence, warmup.MaxShortSequenceLength);
        int sequenceCount = _random.Next(minSequence, maxSequence + 1);

        VideoWatchResult? previous = null;
        for (int i = 0; i < sequenceCount; i++)
        {
            string context = i == 0 ? "Shorts" : "ShortsContinuation";
            string method = i == 0 ? "InitialShort" : "NextShort";
            string detail = $"Shorts#{i + 1}";
            string entryPoint = previous?.EntryPoint ?? "Shorts";

            var result = await _videoPlayer.WatchShortAsync(
                warmup,
                context,
                method,
                detail,
                entryPoint,
                position: i + 1,
                parent: previous);
            if (result == null)
            {
                break;
            }

            previous = result;

            if (i < sequenceCount - 1)
            {
                bool advanced = await AdvanceToNextShortAsync();
                if (!advanced)
                {
                    break;
                }
            }
        }

        return previous != null;
    }

    private async Task<bool> AdvanceToNextShortAsync()
    {
        var page = await _ensurePage();
        try
        {
            string previousUrl = page.Url;
            if (_random.NextDouble() < 0.55)
            {
                await page.Keyboard.PressAsync("ArrowDown");
                await _waitForNavigationAsync(page, previousUrl);
                return true;
            }

            var mouse = _mouseProvider();
            var nextButton = page.Locator("button[aria-label*='Next'], button[aria-label*='Tiếp'], button[aria-label*='Sau']");
            if (await nextButton.CountAsync() > 0 && await nextButton.First.IsEnabledAsync())
            {
                if (mouse != null)
                {
                    await mouse.MoveAndClickAsync(nextButton.First);
                }
                else
                {
                    await nextButton.First.ClickAsync();
                }

                await _waitForNavigationAsync(page, previousUrl);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to advance to next short.");
        }

        return false;
    }

    private async Task<bool> TryEnsureGuideMenuVisibleAsync(IPage page)
    {
        try
        {
            var guideButton = page.Locator("button#guide-button, #guide-button");
            if (await guideButton.CountAsync() == 0)
            {
                return false;
            }

            var target = guideButton.First;
            if (await target.GetAttributeAsync("pressed") == "true")
            {
                return true;
            }

            var mouse = _mouseProvider();
            if (mouse != null)
            {
                await mouse.MoveAndClickAsync(target);
            }
            else
            {
                await target.ClickAsync();
            }

            await Task.Delay(_random.Next(600, 1200));
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to toggle guide menu for shorts.");
            return false;
        }
    }

    private async Task<ILocator?> TryFindShortsLinkAsync(IPage page)
    {
        try
        {
            var locator = page.Locator("ytd-mini-guide-entry-renderer:nth-child(2) a, a[title='Shorts'], a[title='SHORTS']");
            if (await locator.CountAsync() == 0)
            {
                return null;
            }

            return locator.First;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to locate shorts link.");
            return null;
        }
    }
}
