using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class HomeInteractionService
{
    private readonly Func<Task<IPage>> _ensurePage;
    private readonly Func<YouTubeWarmupSettings, Task> _navigateHomeAsync;
    private readonly VideoPlayerService _videoPlayer;
    private readonly RecommendationChainService _recommendationChain;
    private readonly Func<IPage, string, Task> _waitForNavigationAsync;
    private readonly Random _random;
    private readonly Func<MouseHelper?> _mouseProvider;
    private readonly ILogger? _logger;

    public HomeInteractionService(
        Func<Task<IPage>> ensurePage,
        Func<YouTubeWarmupSettings, Task> navigateHomeAsync,
        VideoPlayerService videoPlayer,
        RecommendationChainService recommendationChain,
        Func<IPage, string, Task> waitForNavigationAsync,
        Random random,
        Func<MouseHelper?> mouseProvider,
        ILogger? logger)
    {
        _ensurePage = ensurePage;
        _navigateHomeAsync = navigateHomeAsync;
        _videoPlayer = videoPlayer;
        _recommendationChain = recommendationChain;
        _waitForNavigationAsync = waitForNavigationAsync;
        _random = random;
        _mouseProvider = mouseProvider;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(YouTubeWarmupSettings warmup)
    {
        try
        {
            var page = await _ensurePage();
            await _navigateHomeAsync(warmup);
            await MaybeMisclickMenuAsync(page);

            var richItems = page.Locator("#contents ytd-rich-item-renderer a#thumbnail");
            int count = await richItems.CountAsync();
            if (count == 0)
            {
                _logger?.LogWarning("No rich items on YouTube home page.");
                return false;
            }

            int index = _random.Next(0, count);
            var target = richItems.Nth(index);
            var mouse = _mouseProvider();
            if (mouse != null)
            {
                await mouse.MoveAndClickAsync(target);
            }
            else
            {
                await target.ClickAsync();
            }

            string beforeClickUrl = page.Url;
            await _waitForNavigationAsync(page, beforeClickUrl);

            var result = await _videoPlayer.WatchCurrentEntryAsync(
                warmup,
                context: "RecommendationHome",
                method: "HomeRecommendation",
                contextDetail: $"HomeRecommendation#{index + 1}",
                entryPoint: "Home",
                keyword: null,
                position: index + 1,
                parent: null);
            if (result == null)
            {
                return false;
            }

            await _recommendationChain.MaybeChainRecommendedVideosAsync(warmup, result);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Home recommendation interaction failed.");
            return false;
        }
    }

    private async Task MaybeMisclickMenuAsync(IPage page)
    {
        if (_random.NextDouble() > 0.15)
        {
            return;
        }

        try
        {
            var menuItems = page.Locator("ytd-mini-guide-entry-renderer a");
            int menuCount = await menuItems.CountAsync();
            if (menuCount == 0)
            {
                return;
            }

            int index = _random.Next(0, Math.Min(menuCount, 4));
            var target = menuItems.Nth(index);
            var mouse = _mouseProvider();
            if (mouse != null)
            {
                await mouse.MoveAndClickAsync(target);
            }
            else
            {
                await target.ClickAsync();
            }

            await Task.Delay(_random.Next(600, 1400));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed during menu misclick simulation on home page.");
        }
    }
}
