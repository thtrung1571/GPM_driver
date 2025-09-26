using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class RecommendationChainService
{
    private readonly Func<Task<IPage>> _ensurePage;
    private readonly Func<MouseHelper?> _mouseProvider;
    private readonly Random _random;
    private readonly ILogger? _logger;
    private readonly VideoPlayerService _videoPlayer;
    private readonly Func<IPage, string, Task> _waitForNavigationAsync;

    public RecommendationChainService(
        Func<Task<IPage>> ensurePage,
        Func<MouseHelper?> mouseProvider,
        Random random,
        ILogger? logger,
        VideoPlayerService videoPlayer,
        Func<IPage, string, Task> waitForNavigationAsync)
    {
        _ensurePage = ensurePage;
        _mouseProvider = mouseProvider;
        _random = random;
        _logger = logger;
        _videoPlayer = videoPlayer;
        _waitForNavigationAsync = waitForNavigationAsync;
    }

    public async Task MaybeChainRecommendedVideosAsync(YouTubeWarmupSettings warmup, VideoWatchResult parent)
    {
        if (parent.VideoType == YouTubeUrlHelper.YouTubeVideoKind.Short)
        {
            await ChainShortsAsync(warmup, parent);
            return;
        }

        int maxChain = Math.Clamp(warmup.MaxRecommendationDepth, 0, 5);
        if (maxChain <= 0)
        {
            return;
        }

        var page = await _ensurePage();
        VideoWatchResult? current = parent;

        for (int i = 0; i < maxChain; i++)
        {
            string previousUrl = page.Url;
            bool navigated;
            string method;
            string contextDetail;
            int? position = null;

            bool useAutoplay = i == 0 && _random.NextDouble() < Math.Clamp(warmup.AutoplayFollowProbability, 0, 1);
            if (useAutoplay)
            {
                navigated = await TriggerAutoplayAsync(page);
                method = "Autoplay";
                contextDetail = "AutoplayNext";
            }
            else
            {
                int? selection = await OpenRecommendedSidebarVideoAsync(page);
                navigated = selection.HasValue;
                method = "ManualSidebar";
                if (!navigated)
                {
                    break;
                }

                position = selection!.Value + 1;
                contextDetail = $"Sidebar#{position}";
            }

            if (!navigated)
            {
                break;
            }

            await _waitForNavigationAsync(page, previousUrl);
            if (string.Equals(previousUrl, page.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Recommended navigation did not change URL. Ending chain early.");
                break;
            }

            var result = await _videoPlayer.WatchCurrentEntryAsync(
                warmup,
                context: "RecommendationNext",
                method: method,
                contextDetail: contextDetail,
                entryPoint: current?.EntryPoint ?? "Recommendation",
                keyword: null,
                position: position,
                parent: current);
            if (result == null)
            {
                break;
            }

            current = result;
        }
    }

    private async Task ChainShortsAsync(YouTubeWarmupSettings warmup, VideoWatchResult parent)
    {
        var page = await _ensurePage();
        VideoWatchResult? current = parent;

        for (int i = 0; i < Math.Clamp(warmup.MaxRecommendationDepth, 0, 5); i++)
        {
            string previousUrl = page.Url;
            bool advanced = await TryNavigateShortContinuationAsync(page);
            if (!advanced)
            {
                break;
            }

            await _waitForNavigationAsync(page, previousUrl);

            var result = await _videoPlayer.WatchCurrentEntryAsync(
                warmup,
                context: "RecommendationShort",
                method: "ShortsNavigation",
                contextDetail: $"ShortRecommendation#{i + 1}",
                entryPoint: current?.EntryPoint ?? "Shorts",
                keyword: null,
                position: i + 1,
                parent: current);

            if (result == null)
            {
                break;
            }

            current = result;
        }
    }

    private async Task<bool> TryNavigateShortContinuationAsync(IPage page)
    {
        try
        {
            string previousUrl = page.Url;
            var mouse = _mouseProvider();
            var downButton = page.Locator("#navigation-button-down button, #navigation-button-down tp-yt-paper-button, #navigation-button-down");
            if (await downButton.CountAsync() > 0 && await downButton.First.IsVisibleAsync())
            {
                if (mouse != null)
                {
                    await mouse.MoveAndClickAsync(downButton.First);
                }
                else
                {
                    await downButton.First.ClickAsync();
                }
            }
            else
            {
                await page.Keyboard.PressAsync("ArrowDown");
            }

            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(400);
                if (!string.Equals(previousUrl, page.Url, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to navigate to next short during recommendation chain.");
        }

        return false;
    }

    private async Task<bool> TriggerAutoplayAsync(IPage page)
    {
        try
        {
            await Task.Delay(_random.Next(1800, 3200));
            if (_random.NextDouble() < 0.6)
            {
                await page.Keyboard.PressAsync("Shift+N");
            }
            else
            {
                var mouse = _mouseProvider();
                var nextButton = page.Locator("a[aria-label*='Next'], button[aria-label*='Next']");
                if (await nextButton.CountAsync() > 0)
                {
                    if (mouse != null)
                    {
                        await mouse.MoveAndClickAsync(nextButton.First);
                    }
                    else
                    {
                        await nextButton.First.ClickAsync();
                    }
                }
                else
                {
                    await page.Keyboard.PressAsync("Shift+N");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to trigger autoplay navigation.");
            return false;
        }
    }

    private async Task<int?> OpenRecommendedSidebarVideoAsync(IPage page)
    {
        try
        {
            var recTiles = page.Locator(
                "#items > yt-lockup-view-model a#thumbnail, " +
                "ytd-compact-video-renderer a#thumbnail, " +
                "ytd-watch-next-secondary-results-renderer a#thumbnail");
            int count = await recTiles.CountAsync();
            if (count == 0)
            {
                return null;
            }

            int selectionCount = Math.Min(count, 6);
            int index = _random.Next(0, selectionCount);
            var target = recTiles.Nth(index);
            await ScrollHelper.ScrollToElementAsync(page, target);
            var mouse = _mouseProvider();
            if (mouse != null)
            {
                await mouse.MoveAndClickAsync(target);
            }
            else
            {
                await target.ClickAsync();
            }

            return index;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to open recommended video from sidebar.");
            return null;
        }
    }
}
