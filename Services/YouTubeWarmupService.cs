using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services;

internal class YouTubeWarmupService
{
    private readonly IBrowserContext _context;
    private readonly ILogger<YouTubeWarmupService>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    private IPage? _page;
    private MouseHelper? _mouseHelper;
    private KeyboardHelper? _keyboardHelper;
    private bool _isFirstRun = true;

    private static readonly string[] FallbackKeywords = new[]
    {
        "daily vlog",
        "music playlist",
        "travel guide",
        "technology review",
        "recipe tutorial",
        "gaming highlights"
    };

    public YouTubeWarmupService(IBrowserContext context, ILogger<YouTubeWarmupService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task RunWarmupAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        if (warmup is null)
        {
            throw new ArgumentNullException(nameof(warmup));
        }

        if (!warmup.Enabled)
        {
            _logger?.LogInformation("YouTube warmup disabled. Skipping.");
            return;
        }

        if (warmup.MinInteractions < 1)
        {
            warmup.MinInteractions = 1;
        }

        if (warmup.MaxInteractions < warmup.MinInteractions)
        {
            warmup.MaxInteractions = warmup.MinInteractions;
        }

        if (warmup.MinWatchMilliseconds <= 0 || warmup.MinWatchMilliseconds > warmup.MaxWatchMilliseconds)
        {
            warmup.MinWatchMilliseconds = Math.Min(15000, warmup.MaxWatchMilliseconds);
        }

        if (warmup.MinShortWatchMilliseconds <= 0 || warmup.MinShortWatchMilliseconds > warmup.MaxShortWatchMilliseconds)
        {
            warmup.MinShortWatchMilliseconds = Math.Min(8000, warmup.MaxShortWatchMilliseconds);
        }

        int completed = 0;
        while (true)
        {
            try
            {
                await EnsureOnYouTubeAsync(warmup);
                await PerformInteractionAsync(keywordDirectory, warmup);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "YouTube interaction failed.");
            }

            _isFirstRun = false;
            completed++;

            if (completed >= warmup.MaxInteractions)
            {
                _logger?.LogInformation("Reached max YouTube interactions ({Count}). Ending warmup.", completed);
                break;
            }

            if (completed < warmup.MinInteractions)
            {
                _logger?.LogInformation("Completed {Count} YouTube interactions but minimum is {Minimum}. Continuing.", completed, warmup.MinInteractions);
                await PauseBetweenActionsAsync(warmup);
                continue;
            }

            double roll = _random.NextDouble();
            if (roll >= warmup.ContinueProbability)
            {
                _logger?.LogInformation("Stopping YouTube warmup after {Count} interactions (roll={Roll:0.00} threshold={Threshold:0.00}).", completed, roll, warmup.ContinueProbability);
                break;
            }

            _logger?.LogInformation("Continuing YouTube warmup after {Count} interactions (roll={Roll:0.00}).", completed, roll);
            await PauseBetweenActionsAsync(warmup);
        }
    }

    private async Task PerformInteractionAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        double normalizedSearchWeight = Math.Clamp(warmup.SearchWeight, 0, 1);
        double normalizedShortsWeight = Math.Clamp(warmup.ShortsWeight, 0, 1 - normalizedSearchWeight);
        double homeWeight = 1 - normalizedSearchWeight - normalizedShortsWeight;

        double roll = _random.NextDouble();
        if (_isFirstRun)
        {
            roll = Math.Min(roll, normalizedSearchWeight + 0.1);
        }

        if (roll < normalizedSearchWeight)
        {
            _logger?.LogInformation("YouTube warmup selected search interaction.");
            await SearchAndWatchAsync(keywordDirectory, warmup);
            return;
        }

        if (roll < normalizedSearchWeight + normalizedShortsWeight)
        {
            _logger?.LogInformation("YouTube warmup selected Shorts interaction.");
            bool success = await OpenShortsAsync(warmup);
            if (!success)
            {
                _logger?.LogInformation("Shorts interaction unavailable; falling back to home recommendation.");
                await WatchFromHomeAsync(warmup);
            }
            return;
        }

        _logger?.LogInformation("YouTube warmup selected home recommendation interaction (weight={Weight:0.00}).", homeWeight);
        await WatchFromHomeAsync(warmup);
    }

    private async Task<IPage> EnsurePageAsync()
    {
        if (_page != null && !_page.IsClosed)
        {
            return _page;
        }

        var existing = _context.Pages.FirstOrDefault(p => !p.IsClosed);
        _page = existing ?? await _context.NewPageAsync();
        _mouseHelper = new MouseHelper(_page);
        _keyboardHelper = new KeyboardHelper(_page);
        return _page;
    }

    private async Task EnsureOnYouTubeAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        string? url = page.Url;
        if (!IsOnYouTube(url))
        {
            string domain = ChooseDomain(warmup);
            _logger?.LogInformation("Navigating to YouTube domain {Domain} (current={Url}).", domain, url ?? "unknown");
            await page.GotoAsync(domain, new PageGotoOptions
            {
                Timeout = 60000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(_random.Next(1000, 2500));
        }
    }

    private async Task SearchAndWatchAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        await EnsureOnYouTubeAsync(warmup);

        string keyword = await PickKeywordAsync(keywordDirectory);
        _logger?.LogInformation("Searching YouTube for '{Keyword}'.", keyword);

        var input = await FocusSearchBoxAsync();
        await ClearInputAsync(input);
        await _keyboardHelper!.TypeLikeHumanAsync(input, keyword);
        await Task.Delay(_random.Next(400, 1200));

        double decision = _random.NextDouble();
        if (decision < 0.4)
        {
            var searchButton = page.Locator("button#search-icon-legacy");
            if (await searchButton.CountAsync() > 0 && await searchButton.IsVisibleAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(searchButton);
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
            await _mouseHelper!.MoveAndClickAsync(input);
            await input.PressAsync("Enter");
        }

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(_random.Next(1200, 2500));

        var videoTiles = page.Locator("ytd-video-renderer a#thumbnail, ytd-grid-video-renderer a#thumbnail");
        if (await videoTiles.CountAsync() == 0)
        {
            _logger?.LogWarning("No YouTube search results were found.");
            return;
        }

        int index = _random.Next(0, await videoTiles.CountAsync());
        var target = videoTiles.Nth(index);
        _logger?.LogInformation("Opening YouTube search result #{Index}.", index + 1);
        await ScrollHelper.ScrollToElementAsync(page, target);
        await _mouseHelper!.MoveAndClickAsync(target);

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WatchVideoAsync(warmup);
    }

    private async Task WatchFromHomeAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        await NavigateHomeAsync(warmup);

        var richItems = page.Locator("#contents ytd-rich-item-renderer a#thumbnail");
        int count = await richItems.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("No rich items on YouTube home page. Trying search fallback.");
            await SearchAndWatchAsync(null, warmup);
            return;
        }

        int index = _random.Next(0, count);
        var target = richItems.Nth(index);
        _logger?.LogInformation("Opening YouTube home recommendation #{Index} of {Total}.", index + 1, count);
        await ScrollHelper.ScrollToElementAsync(page, target);
        await _mouseHelper!.MoveAndClickAsync(target);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WatchVideoAsync(warmup);
    }

    private async Task<bool> OpenShortsAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        await EnsureOnYouTubeAsync(warmup);

        var shortsLink = page.Locator("ytd-mini-guide-entry-renderer a[href*='shorts'], a[title='Shorts']");
        if (!await shortsLink.IsVisibleAsync())
        {
            var menuButton = page.Locator("button#guide-button, #guide-button");
            if (await menuButton.CountAsync() > 0 && await menuButton.IsVisibleAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(menuButton);
                await Task.Delay(_random.Next(600, 1200));
            }
        }

        if (!await shortsLink.IsVisibleAsync())
        {
            return false;
        }

        await _mouseHelper!.MoveAndClickAsync(shortsLink);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(_random.Next(1000, 2000));

        var shortsTiles = page.Locator("ytd-reel-video-renderer a#thumbnail, ytd-reel-item-renderer a#thumbnail");
        int count = await shortsTiles.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("No shorts available after navigating to Shorts page.");
            return true;
        }

        int index = _random.Next(0, count);
        var target = shortsTiles.Nth(index);
        _logger?.LogInformation("Opening YouTube short #{Index} of {Total}.", index + 1, count);
        await _mouseHelper!.MoveAndClickAsync(target);
        await page.WaitForTimeoutAsync(_random.Next(warmup.MinShortWatchMilliseconds, warmup.MaxShortWatchMilliseconds + 1));
        return true;
    }

    private async Task WatchVideoAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        int watchMs = _random.Next(warmup.MinWatchMilliseconds, warmup.MaxWatchMilliseconds + 1);
        _logger?.LogInformation("Watching YouTube video for approximately {Seconds:0.0} seconds.", watchMs / 1000.0);
        await page.WaitForTimeoutAsync(watchMs);

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

        if (_random.NextDouble() < 0.25)
        {
            var showMore = page.Locator("#expand, tp-yt-paper-button#expand, #more");
            if (await showMore.CountAsync() > 0 && await showMore.IsVisibleAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(showMore);
            }
        }
    }

    private async Task NavigateHomeAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        string domain = ChooseDomain(warmup);
        if (!page.Url.StartsWith(domain, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("Navigating to YouTube home domain {Domain}.", domain);
            await page.GotoAsync(domain, new PageGotoOptions
            {
                Timeout = 60000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await Task.Delay(_random.Next(800, 1600));
            return;
        }

        var homeLink = page.Locator("ytd-mini-guide-entry-renderer a[title='Home'], ytd-mini-guide-entry-renderer:nth-child(1) a");
        if (await homeLink.IsVisibleAsync())
        {
            await _mouseHelper!.MoveAndClickAsync(homeLink);
            await Task.Delay(_random.Next(800, 1600));
        }
    }

    private async Task<ILocator> FocusSearchBoxAsync()
    {
        var page = await EnsurePageAsync();
        var input = page.Locator("input#search, input[name='search_query']");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });

        bool isFocused = false;
        try
        {
            isFocused = await input.EvaluateAsync<bool>("el => document.activeElement === el");
        }
        catch
        {
            isFocused = false;
        }

        if (!isFocused)
        {
            await _mouseHelper!.MoveAndClickAsync(input);
        }

        return input;
    }

    private async Task ClearInputAsync(ILocator input)
    {
        string value = string.Empty;
        try
        {
            value = await input.InputValueAsync() ?? string.Empty;
        }
        catch
        {
            value = string.Empty;
        }

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (_random.NextDouble() < 0.6)
        {
            await _page!.Keyboard.PressAsync("Control+A");
            await Task.Delay(_random.Next(80, 200));
            await _page.Keyboard.PressAsync("Backspace");
        }
        else
        {
            await _keyboardHelper!.BackspaceAsync(value.Length, fastCorrection: true);
        }
    }

    private async Task<string> PickKeywordAsync(string? keywordDirectory)
    {
        if (string.IsNullOrWhiteSpace(keywordDirectory) || !Directory.Exists(keywordDirectory))
        {
            return FallbackKeywords[_random.Next(FallbackKeywords.Length)];
        }

        try
        {
            var files = Directory.GetFiles(keywordDirectory, "*.txt");
            if (files.Length == 0)
            {
                return FallbackKeywords[_random.Next(FallbackKeywords.Length)];
            }

            var file = files[_random.Next(files.Length)];
            var keywords = (await File.ReadAllLinesAsync(file))
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            if (keywords.Length == 0)
            {
                return FallbackKeywords[_random.Next(FallbackKeywords.Length)];
            }

            return keywords[_random.Next(keywords.Length)];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falling back to default YouTube keywords.");
            return FallbackKeywords[_random.Next(FallbackKeywords.Length)];
        }
    }

    private async Task PauseBetweenActionsAsync(YouTubeWarmupSettings warmup)
    {
        int minDelay = Math.Max(500, warmup.MinDelayBetweenActionsMs);
        int maxDelay = Math.Max(minDelay, warmup.MaxDelayBetweenActionsMs);
        int pause = _random.Next(minDelay, maxDelay + 1);
        await Task.Delay(pause);
    }

    private static bool IsOnYouTube(string? url)
    {
        return !string.IsNullOrEmpty(url) && url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    private string ChooseDomain(YouTubeWarmupSettings warmup)
    {
        var domains = warmup.Domains?.Length > 0
            ? warmup.Domains
            : new[] { "https://www.youtube.com" };
        return domains[_random.Next(domains.Length)];
    }
}
