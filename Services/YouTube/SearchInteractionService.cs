using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class SearchInteractionService
{
    private readonly Func<Task<IPage>> _ensurePage;
    private readonly Func<YouTubeWarmupSettings, Task> _ensureOnYouTube;
    private readonly Func<string?, Task<string>> _pickKeywordAsync;
    private readonly Action<string> _registerKeywordUsage;
    private readonly Func<Task<ILocator>> _focusSearchBoxAsync;
    private readonly Func<ILocator, Task> _clearInputAsync;
    private readonly Func<IPage, string, Task> _waitForNavigationAsync;
    private readonly VideoPlayerService _videoPlayer;
    private readonly RecommendationChainService _recommendationChain;
    private readonly Random _random;
    private readonly Func<MouseHelper?> _mouseProvider;
    private readonly Func<KeyboardHelper?> _keyboardProvider;
    private readonly ILogger? _logger;
    private readonly Func<YouTubeWarmupSettings, Task<bool>> _homeFallback;

    public SearchInteractionService(
        Func<Task<IPage>> ensurePage,
        Func<YouTubeWarmupSettings, Task> ensureOnYouTube,
        Func<string?, Task<string>> pickKeywordAsync,
        Action<string> registerKeywordUsage,
        Func<Task<ILocator>> focusSearchBoxAsync,
        Func<ILocator, Task> clearInputAsync,
        Func<IPage, string, Task> waitForNavigationAsync,
        VideoPlayerService videoPlayer,
        RecommendationChainService recommendationChain,
        Random random,
        Func<MouseHelper?> mouseProvider,
        Func<KeyboardHelper?> keyboardProvider,
        ILogger? logger,
        Func<YouTubeWarmupSettings, Task<bool>> homeFallback)
    {
        _ensurePage = ensurePage;
        _ensureOnYouTube = ensureOnYouTube;
        _pickKeywordAsync = pickKeywordAsync;
        _registerKeywordUsage = registerKeywordUsage;
        _focusSearchBoxAsync = focusSearchBoxAsync;
        _clearInputAsync = clearInputAsync;
        _waitForNavigationAsync = waitForNavigationAsync;
        _videoPlayer = videoPlayer;
        _recommendationChain = recommendationChain;
        _random = random;
        _mouseProvider = mouseProvider;
        _keyboardProvider = keyboardProvider;
        _logger = logger;
        _homeFallback = homeFallback;
    }

    public async Task<bool> ExecuteAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool success = await RunSingleSearchAsync(keywordDirectory, warmup, attempt);
            if (success)
            {
                return true;
            }

            await Task.Delay(_random.Next(1200, 2200));
        }

        _logger?.LogInformation("All YouTube search retries failed; falling back to home recommendations.");
        return await _homeFallback(warmup);
    }

    private async Task<bool> RunSingleSearchAsync(string? keywordDirectory, YouTubeWarmupSettings warmup, int attempt)
    {
        try
        {
            var page = await _ensurePage();
            await _ensureOnYouTube(warmup);

            string keyword = await _pickKeywordAsync(keywordDirectory);
            _registerKeywordUsage(keyword);
            _logger?.LogInformation("YouTube search attempt {Attempt} using keyword '{Keyword}'.", attempt + 1, keyword);

            var input = await _focusSearchBoxAsync();
            await _clearInputAsync(input);
            var keyboardHelper = _keyboardProvider();
            if (keyboardHelper != null)
            {
                await keyboardHelper.TypeLikeHumanAsync(input, keyword);
            }
            else
            {
                await input.TypeAsync(keyword);
            }

            await Task.Delay(_random.Next(400, 1200));
            await MaybeMisclickFilterAsync(page);

            double decision = _random.NextDouble();
            string previousUrl = page.Url;
            if (decision < 0.4)
            {
                var searchButton = page.Locator("button#search-icon-legacy");
                if (await searchButton.CountAsync() > 0 && await searchButton.IsVisibleAsync())
                {
                    var mouse = _mouseProvider();
                    if (mouse != null)
                    {
                        await mouse.MoveAndClickAsync(searchButton);
                    }
                    else
                    {
                        await searchButton.ClickAsync();
                    }
                }
                else
                {
                    await input.PressAsync("Enter");
                }
            }
            else if (decision < 0.75)
            {
                await input.PressAsync("Enter");
            }
            else
            {
                var mouse = _mouseProvider();
                if (mouse != null)
                {
                    await mouse.MoveAndClickAsync(input);
                }
                await input.PressAsync("Enter");
            }

            await _waitForNavigationAsync(page, previousUrl);

            var videoTiles = page.Locator("ytd-video-renderer a#thumbnail, ytd-grid-video-renderer a#thumbnail");
            int tileCount = await videoTiles.CountAsync();
            if (tileCount == 0)
            {
                _logger?.LogWarning("YouTube search returned no video results.");
                return false;
            }

            int index = _random.Next(0, tileCount);
            var target = videoTiles.Nth(index);
            var mouseHelper = _mouseProvider();
            if (mouseHelper != null)
            {
                await mouseHelper.MoveAndClickAsync(target);
            }
            else
            {
                await target.ClickAsync();
            }

            string beforeClickUrl = page.Url;
            await _waitForNavigationAsync(page, beforeClickUrl);

            var result = await _videoPlayer.WatchCurrentEntryAsync(
                warmup,
                context: "Search",
                method: "SearchResult",
                contextDetail: $"SearchResult#{index + 1}",
                entryPoint: "Search",
                keyword: keyword,
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
            _logger?.LogWarning(ex, "YouTube search attempt {Attempt} failed due to exception.", attempt + 1);
            return false;
        }
    }

    private async Task MaybeMisclickFilterAsync(IPage page)
    {
        if (_random.NextDouble() > 0.2)
        {
            return;
        }

        try
        {
            var filtersButton = page.Locator("ytd-toggle-button-renderer a[aria-label*='Filters'], ytd-search-sub-menu-renderer button");
            if (await filtersButton.CountAsync() == 0)
            {
                return;
            }

            var mouse = _mouseProvider();
            if (mouse != null)
            {
                await mouse.MoveAndClickAsync(filtersButton.First);
            }
            else
            {
                await filtersButton.First.ClickAsync();
            }

            await Task.Delay(_random.Next(600, 1200));
            if (_random.NextDouble() < 0.5)
            {
                await page.Keyboard.PressAsync("Escape");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed during misclick simulation before search.");
        }
    }
}
