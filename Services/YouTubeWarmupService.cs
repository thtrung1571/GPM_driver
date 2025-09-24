using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    private IdentityProfile? _identityProfile;
    private List<string>? _identityKeywords;

    private static readonly string[] FallbackKeywords =
    {
        "daily vlog",
        "music playlist",
        "travel guide",
        "technology review",
        "recipe tutorial",
        "gaming highlights"
    };

    private sealed class IdentityProfile
    {
        public string IdentityKey { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class VideoWatchResult
    {
        public string Context { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string? Url { get; set; }
            = string.Empty;
        public string? Title { get; set; }
            = string.Empty;
        public string? ChannelName { get; set; }
            = string.Empty;
        public int PlannedWatchDurationMs { get; set; }
            = 0;
        public int ActualWatchDurationMs { get; set; }
            = 0;
        public DateTimeOffset StartedAt { get; set; }
            = DateTimeOffset.UtcNow;
        public bool IsShort { get; set; }
            = false;
    }

    public YouTubeWarmupService(IBrowserContext context, ILogger<YouTubeWarmupService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task RunWarmupAsync(string? keywordDirectory, YouTubeWarmupSettings warmup, string? profileKey)
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

        await EnsureIdentityKeywordsAsync(keywordDirectory, warmup, profileKey);

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
        _logger?.LogInformation(
            "YouTube identity {Identity} searching for keyword '{Keyword}'.",
            _identityProfile?.IdentityKey ?? "default",
            keyword);

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
        var result = await WatchVideoAsync(warmup, "Search", "SearchResult");
        if (result != null)
        {
            await MaybeChainRecommendedVideosAsync(warmup, result);
        }
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
        var result = await WatchVideoAsync(warmup, "Home", "HomeRecommendation");
        if (result != null)
        {
            await MaybeChainRecommendedVideosAsync(warmup, result);
        }
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

        return await WatchShortsSequenceAsync(warmup);
    }

    private async Task<bool> WatchShortsSequenceAsync(YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
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
        await Task.Delay(_random.Next(800, 1500));

        int minSequence = Math.Max(1, warmup.MinShortSequenceLength);
        int maxSequence = Math.Max(minSequence, warmup.MaxShortSequenceLength);
        int sequenceCount = _random.Next(minSequence, maxSequence + 1);

        for (int i = 0; i < sequenceCount; i++)
        {
            var result = await WatchShortAsync(warmup, i == 0 ? "Shorts" : "ShortsContinuation", i == 0 ? "InitialShort" : "NextShort");
            if (result == null)
            {
                break;
            }

            if (i < sequenceCount - 1)
            {
                bool advanced = await AdvanceToNextShortAsync();
                if (!advanced)
                {
                    break;
                }
            }
        }

        return true;
    }

    private async Task<bool> AdvanceToNextShortAsync()
    {
        var page = await EnsurePageAsync();
        try
        {
            if (_random.NextDouble() < 0.55)
            {
                await page.Keyboard.PressAsync("ArrowDown");
                await Task.Delay(_random.Next(600, 1000));
                return true;
            }

            var nextButton = page.Locator("button[aria-label*='Next'], button[aria-label*='Tiếp'], button[aria-label*='Sau']");
            if (await nextButton.CountAsync() > 0 && await nextButton.First.IsEnabledAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(nextButton.First);
                await Task.Delay(_random.Next(600, 1000));
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to advance to next short.");
        }

        return false;
    }

    private async Task<VideoWatchResult?> WatchVideoAsync(YouTubeWarmupSettings warmup, string context, string method)
    {
        var page = await EnsurePageAsync();
        try
        {
            await page.WaitForSelectorAsync("ytd-player video", new PageWaitForSelectorOptions { Timeout = 20000 });
        }
        catch
        {
            _logger?.LogWarning("Video element did not appear for context {Context}.", context);
        }

        int planned = PlanWatchDuration(warmup.MinWatchMilliseconds, warmup.MaxWatchMilliseconds);
        int actualTarget = ApplyWatchDurationJitter(planned, warmup.MinWatchMilliseconds, warmup.MaxWatchMilliseconds);
        var start = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

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

        if (_random.NextDouble() < 0.25)
        {
            var showMore = page.Locator("#expand, tp-yt-paper-button#expand, #more");
            if (await showMore.CountAsync() > 0 && await showMore.IsVisibleAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(showMore);
            }
        }

        stopwatch.Stop();

        var result = new VideoWatchResult
        {
            Context = context,
            Method = method,
            Url = page.Url,
            Title = await TryGetInnerTextAsync(page.Locator("h1 yt-formatted-string")),
            ChannelName = await TryGetInnerTextAsync(page.Locator("#channel-name a, #owner-name a")),
            PlannedWatchDurationMs = planned,
            ActualWatchDurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
            StartedAt = start,
            IsShort = false
        };

        LogVideoWatch(result);
        return result;
    }

    private async Task<VideoWatchResult?> WatchShortAsync(YouTubeWarmupSettings warmup, string context, string method)
    {
        var page = await EnsurePageAsync();
        int planned = PlanWatchDuration(warmup.MinShortWatchMilliseconds, warmup.MaxShortWatchMilliseconds);
        int actualTarget = ApplyWatchDurationJitter(planned, warmup.MinShortWatchMilliseconds, warmup.MaxShortWatchMilliseconds);
        var start = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

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
            Method = method,
            Url = page.Url,
            Title = await TryGetInnerTextAsync(page.Locator("#description h1, #info-contents h2, #shorts-player h1")),
            ChannelName = await TryGetInnerTextAsync(page.Locator("#channel-name a, #owner-container a")),
            PlannedWatchDurationMs = planned,
            ActualWatchDurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
            StartedAt = start,
            IsShort = true
        };

        LogVideoWatch(result);
        return result;
    }

    private void LogVideoWatch(VideoWatchResult result)
    {
        _logger?.LogInformation(
            "YouTube watch context={Context} method={Method} url={Url} title={Title} channel={Channel} planned_ms={Planned} actual_ms={Actual} started={Started:o} short={IsShort} identity={Identity}.",
            result.Context,
            result.Method,
            result.Url ?? "unknown",
            string.IsNullOrWhiteSpace(result.Title) ? "(unknown)" : result.Title,
            string.IsNullOrWhiteSpace(result.ChannelName) ? "(unknown)" : result.ChannelName,
            result.PlannedWatchDurationMs,
            result.ActualWatchDurationMs,
            result.StartedAt,
            result.IsShort,
            _identityProfile?.IdentityKey ?? "default");
    }

    private async Task MaybeChainRecommendedVideosAsync(YouTubeWarmupSettings warmup, VideoWatchResult initialResult)
    {
        double probability = Math.Clamp(warmup.RecommendationChainProbability, 0, 1);
        if (_random.NextDouble() > probability)
        {
            return;
        }

        _logger?.LogInformation(
            "Evaluating recommendation chain after video {Url} (context={Context}).",
            initialResult.Url ?? "unknown",
            initialResult.Context);

        int minChain = Math.Max(1, warmup.MinRecommendationChainLength);
        int maxChain = Math.Max(minChain, warmup.MaxRecommendationChainLength);
        int chainCount = _random.Next(minChain, maxChain + 1);

        var page = await EnsurePageAsync();
        for (int i = 0; i < chainCount; i++)
        {
            string previousUrl = page.Url;
            bool useAutoplay = i == 0 && _random.NextDouble() < Math.Clamp(warmup.AutoplayFollowProbability, 0, 1);
            bool navigated;
            string method;

            if (useAutoplay)
            {
                navigated = await TriggerAutoplayAsync(page);
                method = "Autoplay";
            }
            else
            {
                navigated = await OpenRecommendedSidebarVideoAsync(page);
                method = "ManualSidebar";
            }

            if (!navigated)
            {
                break;
            }

            try
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("Timed out waiting for recommended video to load.");
            }

            await Task.Delay(_random.Next(1000, 2200));

            if (string.Equals(previousUrl, page.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Recommended navigation did not change URL. Ending chain early.");
                break;
            }

            var result = await WatchVideoAsync(warmup, "Recommendation", method);
            if (result == null)
            {
                break;
            }
        }
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
                var nextButton = page.Locator("a[aria-label*='Next'], button[aria-label*='Next']");
                if (await nextButton.CountAsync() > 0)
                {
                    await _mouseHelper!.MoveAndClickAsync(nextButton.First);
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

    private async Task<bool> OpenRecommendedSidebarVideoAsync(IPage page)
    {
        try
        {
            var recTiles = page.Locator("ytd-compact-video-renderer a#thumbnail, ytd-watch-next-secondary-results-renderer a#thumbnail");
            int count = await recTiles.CountAsync();
            if (count == 0)
            {
                return false;
            }

            int selectionCount = Math.Min(count, 6);
            int index = _random.Next(0, selectionCount);
            var target = recTiles.Nth(index);
            await ScrollHelper.ScrollToElementAsync(page, target);
            await _mouseHelper!.MoveAndClickAsync(target);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to open recommended video from sidebar.");
            return false;
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

        return _random.Next(Math.Max(mediumUpper, min), max + 1);
    }

    private int ApplyWatchDurationJitter(int planned, int min, int max)
    {
        double jitter = 0.9 + (_random.NextDouble() * 0.3); // 0.9x - 1.2x
        int adjusted = (int)(planned * jitter);
        return Math.Clamp(adjusted, min, max);
    }

    private async Task<string> TryGetInnerTextAsync(ILocator locator)
    {
        try
        {
            if (await locator.CountAsync() == 0)
            {
                return string.Empty;
            }

            string? text = await locator.First.InnerTextAsync();
            return text?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
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
        if (_identityKeywords != null && _identityKeywords.Count > 0)
        {
            return _identityKeywords[_random.Next(_identityKeywords.Count)];
        }

        if (!string.IsNullOrWhiteSpace(keywordDirectory) && Directory.Exists(keywordDirectory))
        {
            try
            {
                var files = Directory.GetFiles(keywordDirectory, "*.txt");
                if (files.Length > 0)
                {
                    var file = files[_random.Next(files.Length)];
                    var keywords = (await File.ReadAllLinesAsync(file))
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToArray();

                    if (keywords.Length > 0)
                    {
                        return keywords[_random.Next(keywords.Length)];
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falling back to default YouTube keywords.");
            }
        }

        return FallbackKeywords[_random.Next(FallbackKeywords.Length)];
    }

    private async Task EnsureIdentityKeywordsAsync(string? keywordDirectory, YouTubeWarmupSettings warmup, string? profileKey)
    {
        if (_identityKeywords != null && _identityKeywords.Count > 0)
        {
            return;
        }

        string identityKey = string.IsNullOrWhiteSpace(profileKey) ? "default" : profileKey;
        string baseDirectory = string.IsNullOrWhiteSpace(warmup.IdentityCacheDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "youtube-identities")
            : Path.GetFullPath(warmup.IdentityCacheDirectory);

        Directory.CreateDirectory(baseDirectory);
        string fileName = SanitizeFileName(identityKey) + ".json";
        string identityPath = Path.Combine(baseDirectory, fileName);

        if (File.Exists(identityPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<IdentityProfile>(await File.ReadAllTextAsync(identityPath));
                if (existing != null && existing.Keywords.Count > 0)
                {
                    _identityProfile = existing;
                    _identityKeywords = existing.Keywords;
                    _logger?.LogInformation(
                        "Loaded YouTube identity {Identity} with {KeywordCount} keywords from {Path}.",
                        existing.IdentityKey,
                        existing.Keywords.Count,
                        identityPath);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read YouTube identity file {Path}. Rebuilding.", identityPath);
            }
        }

        var profile = await BuildIdentityProfileAsync(identityKey, keywordDirectory, warmup);
        _identityProfile = profile;
        _identityKeywords = profile.Keywords;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(identityPath, JsonSerializer.Serialize(profile, options));
            _logger?.LogInformation(
                "Created YouTube identity {Identity} with {KeywordCount} keywords (sources={Sources}).",
                profile.IdentityKey,
                profile.Keywords.Count,
                profile.SourceFiles.Count == 0 ? "fallback" : string.Join(",", profile.SourceFiles));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist YouTube identity profile to {Path}.", identityPath);
        }
    }

    private async Task<IdentityProfile> BuildIdentityProfileAsync(string identityKey, string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        var profile = new IdentityProfile
        {
            IdentityKey = identityKey,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(keywordDirectory) && Directory.Exists(keywordDirectory))
        {
            try
            {
                var files = Directory.GetFiles(keywordDirectory, "*.txt");
                if (files.Length > 0)
                {
                    int selectionCount = Math.Clamp(warmup.IdentityKeywordFileCount, 1, files.Length);
                    var selected = files.OrderBy(_ => _random.NextDouble()).Take(selectionCount).ToArray();

                    foreach (var file in selected)
                    {
                        var keywords = (await File.ReadAllLinesAsync(file))
                            .Select(line => line.Trim())
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToArray();

                        if (keywords.Length > 0)
                        {
                            profile.Keywords.AddRange(keywords);
                            profile.SourceFiles.Add(Path.GetFileName(file));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to build identity keywords from directory {Directory}.", keywordDirectory);
            }
        }

        if (profile.Keywords.Count == 0)
        {
            profile.Keywords.AddRange(FallbackKeywords);
        }
        else
        {
            profile.Keywords = profile.Keywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => keyword.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return profile;
    }

    private static string SanitizeFileName(string identityKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(identityKey.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "identity" : sanitized;
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
