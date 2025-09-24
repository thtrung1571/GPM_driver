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

    private YouTubeIdentityDocument? _identityDocument;
    private readonly List<string> _identityKeywords = new();
    private string _identityKey = "default";
    private string? _identityDirectory;
    private string? _sessionsDirectory;
    private string? _identityPath;

    private readonly List<SessionInteraction> _sessionInteractions = new();
    private readonly Dictionary<string, int> _sessionSourceCounts = new(StringComparer.OrdinalIgnoreCase);
    private string _sessionId = string.Empty;
    private DateTimeOffset _sessionStart;
    private long _sessionTotalWatchTimeMs;
    private int _sessionBounceCount;

    private static readonly string[] FallbackKeywords =
    {
        "daily vlog",
        "music playlist",
        "travel guide",
        "technology review",
        "recipe tutorial",
        "gaming highlights"
    };

    private sealed class YouTubeIdentityDocument
    {
        public IdentityProfileInfo Profile { get; set; } = new();
        public KeywordSection Keywords { get; set; } = new();
        public IdentityStats Stats { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
    }

    private sealed class IdentityProfileInfo
    {
        public string ProfileId { get; set; } = string.Empty;
        public string Persona { get; set; } = "Generalist";
        public string Region { get; set; } = "Global";
        public string Language { get; set; } = "en-US";
        public string Domain { get; set; } = "https://www.youtube.com";
        public string Timezone { get; set; } = TimeZoneInfo.Utc.Id;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
        public string[] Behaviors { get; set; } = Array.Empty<string>();
    }

    private sealed class KeywordSection
    {
        public List<string> SeedList { get; set; } = new();
        public List<KeywordUsageRecord> UsedHistory { get; set; } = new();
        public List<KeywordVideoRecord> KeywordHistory { get; set; } = new();
    }

    private sealed class KeywordUsageRecord
    {
        public string Keyword { get; set; } = string.Empty;
        public int TimesUsed { get; set; }
            = 0;
        public DateTimeOffset? LastUsed { get; set; }
            = null;
    }

    private sealed class KeywordVideoRecord
    {
        public string Keyword { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
        public int WatchedMs { get; set; }
            = 0;
        public DateTimeOffset WatchedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class IdentityStats
    {
        public int TotalSessions { get; set; }
            = 0;
        public int TotalVideosWatched { get; set; }
            = 0;
        public long TotalWatchTimeMs { get; set; }
            = 0;
        public double AvgWatchDurationMs { get; set; }
            = 0;
        public double BounceRate { get; set; }
            = 0;
        public double DeepSessionRate { get; set; }
            = 0;
        public int BounceVideos { get; set; }
            = 0;
        public int DeepSessions { get; set; }
            = 0;
        public DateTimeOffset? LastSessionTimestamp { get; set; }
            = null;
        public DateTimeOffset? LastWatchedAt { get; set; }
            = null;
        public SourceBreakdown SourceDistribution { get; set; } = new();
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class SourceBreakdown
    {
        public int SearchCount { get; set; }
            = 0;
        public int HomeCount { get; set; }
            = 0;
        public int ShortsCount { get; set; }
            = 0;
        public double SearchPercent { get; set; }
            = 0;
        public double HomePercent { get; set; }
            = 0;
        public double ShortsPercent { get; set; }
            = 0;
    }

    private sealed class VideoWatchResult
    {
        public string Context { get; set; } = string.Empty;
        public string ContextDetail { get; set; } = string.Empty;
        public string EntryPoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int? ResultPosition { get; set; }
            = null;
        public string? Keyword { get; set; }
            = null;
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
        public string? ParentVideoUrl { get; set; }
            = null;
        public string? ParentVideoTitle { get; set; }
            = null;
        public string? ParentContext { get; set; }
            = null;
    }

    private sealed class SessionInteraction
    {
        public string Context { get; set; } = string.Empty;
        public string ContextDetail { get; set; } = string.Empty;
        public string EntryPoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public int? ResultPosition { get; set; }
            = null;
        public string? Keyword { get; set; }
            = null;
        public string? VideoUrl { get; set; }
            = string.Empty;
        public string? Title { get; set; }
            = string.Empty;
        public string? ChannelName { get; set; }
            = string.Empty;
        public int PlannedWatchMs { get; set; }
            = 0;
        public int ActualWatchMs { get; set; }
            = 0;
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public bool IsShort { get; set; }
            = false;
        public string? ParentVideo { get; set; }
            = null;
        public string? ParentVideoTitle { get; set; }
            = null;
        public string? ParentContext { get; set; }
            = null;
    }

    private sealed class SessionLog
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset EndedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<SessionInteraction> Interactions { get; set; } = new();
        public SessionSummary Summary { get; set; } = new();
    }

    private sealed class SessionSummary
    {
        public int VideosWatched { get; set; }
            = 0;
        public long TotalWatchTimeMs { get; set; }
            = 0;
        public double AverageWatchTimeMs { get; set; }
            = 0;
        public int BounceCount { get; set; }
            = 0;
        public bool DeepChain { get; set; }
            = false;
        public SessionSourceBreakdown Sources { get; set; } = new();
    }

    private sealed class SessionSourceBreakdown
    {
        public int Search { get; set; }
            = 0;
        public int Home { get; set; }
            = 0;
        public int Shorts { get; set; }
            = 0;
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

        await EnsureIdentityAsync(keywordDirectory, warmup, profileKey);
        BeginSession();

        try
        {
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
        finally
        {
            await FinalizeSessionAsync();
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
            SetActiveDomain(domain);
            await page.GotoAsync(domain, new PageGotoOptions
            {
                Timeout = 60000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(_random.Next(1000, 2500));
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var uri = new Uri(url);
                string baseDomain = $"{uri.Scheme}://{uri.Host}";
                SetActiveDomain(baseDomain);
            }
            catch
            {
                // ignore parse failures
            }
        }
    }

    private async Task SearchAndWatchAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        var page = await EnsurePageAsync();
        await EnsureOnYouTubeAsync(warmup);

        string keyword = await PickKeywordAsync(keywordDirectory);
        RegisterKeywordUsage(keyword);
        _logger?.LogInformation(
            "YouTube identity {Identity} searching for keyword '{Keyword}'.",
            _identityKey,
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
        var result = await WatchVideoAsync(
            warmup,
            context: "Search",
            method: "SearchResult",
            contextDetail: $"SearchResult#{index + 1}",
            entryPoint: "Search",
            keyword: keyword,
            position: index + 1,
            parent: null);
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
        var result = await WatchVideoAsync(
            warmup,
            context: "Home",
            method: "HomeRecommendation",
            contextDetail: $"HomeRecommendation#{index + 1}",
            entryPoint: "Home",
            keyword: null,
            position: index + 1,
            parent: null);
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

        VideoWatchResult? previous = null;
        for (int i = 0; i < sequenceCount; i++)
        {
            string context = i == 0 ? "Shorts" : "ShortsContinuation";
            string method = i == 0 ? "InitialShort" : "NextShort";
            string detail = $"Shorts#{i + 1}";
            string entryPoint = previous?.EntryPoint ?? "Shorts";

            var result = await WatchShortAsync(
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

    private async Task<VideoWatchResult?> WatchVideoAsync(
        YouTubeWarmupSettings warmup,
        string context,
        string method,
        string contextDetail,
        string entryPoint,
        string? keyword,
        int? position,
        VideoWatchResult? parent)
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
            ParentContext = parent?.ContextDetail ?? parent?.Context
        };

        RecordVideoWatch(result);
        return result;
    }

    private async Task<VideoWatchResult?> WatchShortAsync(
        YouTubeWarmupSettings warmup,
        string context,
        string method,
        string contextDetail,
        string entryPoint,
        int? position,
        VideoWatchResult? parent)
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
            ContextDetail = contextDetail,
            EntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? context : entryPoint,
            Method = method,
            ResultPosition = position,
            Url = page.Url,
            Title = await TryGetInnerTextAsync(page.Locator("#description h1, #info-contents h2, #shorts-player h1")),
            ChannelName = await TryGetInnerTextAsync(page.Locator("#channel-name a, #owner-container a")),
            PlannedWatchDurationMs = planned,
            ActualWatchDurationMs = (int)stopwatch.Elapsed.TotalMilliseconds,
            StartedAt = start,
            IsShort = true,
            ParentVideoUrl = parent?.Url,
            ParentVideoTitle = parent?.Title,
            ParentContext = parent?.ContextDetail ?? parent?.Context
        };

        RecordVideoWatch(result);
        return result;
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
        var parent = initialResult;
        for (int i = 0; i < chainCount; i++)
        {
            string previousUrl = page.Url;
            bool useAutoplay = i == 0 && _random.NextDouble() < Math.Clamp(warmup.AutoplayFollowProbability, 0, 1);
            bool navigated;
            string method;
            string contextDetail;
            int? position = null;

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

            var result = await WatchVideoAsync(
                warmup,
                context: "Recommendation",
                method: method,
                contextDetail: contextDetail,
                entryPoint: parent.EntryPoint,
                keyword: null,
                position: position,
                parent: parent);
            if (result == null)
            {
                break;
            }

            parent = result;
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

    private async Task<int?> OpenRecommendedSidebarVideoAsync(IPage page)
    {
        try
        {
            var recTiles = page.Locator("ytd-compact-video-renderer a#thumbnail, ytd-watch-next-secondary-results-renderer a#thumbnail");
            int count = await recTiles.CountAsync();
            if (count == 0)
            {
                return null;
            }

            int selectionCount = Math.Min(count, 6);
            int index = _random.Next(0, selectionCount);
            var target = recTiles.Nth(index);
            await ScrollHelper.ScrollToElementAsync(page, target);
            await _mouseHelper!.MoveAndClickAsync(target);
            return index;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to open recommended video from sidebar.");
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
        SetActiveDomain(domain);
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
        if (_identityKeywords.Count > 0)
        {
            if (_identityDocument?.Keywords != null)
            {
                var usageLookup = (_identityDocument.Keywords.UsedHistory ?? new List<KeywordUsageRecord>())
                    .GroupBy(record => record.Keyword, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(r => r.LastUsed ?? DateTimeOffset.MinValue).First())
                    .ToDictionary(record => record.Keyword, record => record, StringComparer.OrdinalIgnoreCase);

                var ranked = _identityKeywords
                    .Select(keyword =>
                    {
                        usageLookup.TryGetValue(keyword, out var record);
                        int usage = record?.TimesUsed ?? 0;
                        DateTimeOffset lastUsed = record?.LastUsed ?? DateTimeOffset.MinValue;
                        return new { keyword, usage, lastUsed };
                    })
                    .OrderBy(item => item.usage)
                    .ThenBy(item => item.lastUsed)
                    .ThenBy(_ => _random.NextDouble())
                    .FirstOrDefault();

                if (ranked != null)
                {
                    return ranked.keyword;
                }
            }

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

    private async Task EnsureIdentityAsync(string? keywordDirectory, YouTubeWarmupSettings warmup, string? profileKey)
    {
        if (_identityDocument != null && _identityKeywords.Count > 0)
        {
            return;
        }

        _identityKey = string.IsNullOrWhiteSpace(profileKey) ? "default" : profileKey;
        string baseDirectory = string.IsNullOrWhiteSpace(warmup.IdentityCacheDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "youtube-identities")
            : Path.GetFullPath(warmup.IdentityCacheDirectory);

        Directory.CreateDirectory(baseDirectory);
        string safeKey = SanitizeFileName(_identityKey);
        _identityDirectory = Path.Combine(baseDirectory, safeKey);
        Directory.CreateDirectory(_identityDirectory);
        _sessionsDirectory = Path.Combine(_identityDirectory, "sessions");
        Directory.CreateDirectory(_sessionsDirectory);
        _identityPath = Path.Combine(_identityDirectory, "identity.json");

        YouTubeIdentityDocument? document = null;
        if (File.Exists(_identityPath))
        {
            try
            {
                document = JsonSerializer.Deserialize<YouTubeIdentityDocument>(await File.ReadAllTextAsync(_identityPath));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read YouTube identity document from {Path}. Rebuilding.", _identityPath);
            }
        }

        bool updated = false;
        if (document == null)
        {
            document = await CreateNewIdentityDocumentAsync(keywordDirectory, warmup);
            updated = true;
            _logger?.LogInformation(
                "Created YouTube identity {Identity} with {KeywordCount} keywords (sources={Sources}).",
                _identityKey,
                document.Keywords.SeedList.Count,
                document.SourceFiles.Count == 0 ? "fallback" : string.Join(",", document.SourceFiles));
        }

        document.Profile ??= new IdentityProfileInfo();
        if (!string.Equals(document.Profile.ProfileId, _identityKey, StringComparison.Ordinal))
        {
            document.Profile.ProfileId = _identityKey;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(document.Profile.Persona) && !string.IsNullOrWhiteSpace(warmup.Persona))
        {
            document.Profile.Persona = warmup.Persona;
            updated = true;
        }
        if (string.IsNullOrWhiteSpace(document.Profile.Region) && !string.IsNullOrWhiteSpace(warmup.Region))
        {
            document.Profile.Region = warmup.Region;
            updated = true;
        }
        if (string.IsNullOrWhiteSpace(document.Profile.Language) && !string.IsNullOrWhiteSpace(warmup.Language))
        {
            document.Profile.Language = warmup.Language;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(document.Profile.Domain) && warmup.Domains?.Length > 0)
        {
            document.Profile.Domain = warmup.Domains[0];
            updated = true;
        }

        string configuredTimezone = warmup.Timezone ?? TimeZoneInfo.Local.Id;
        if (string.IsNullOrWhiteSpace(document.Profile.Timezone) || !string.Equals(document.Profile.Timezone, configuredTimezone, StringComparison.Ordinal))
        {
            document.Profile.Timezone = configuredTimezone;
            updated = true;
        }

        if (warmup.Behaviors != null && warmup.Behaviors.Length > 0 && (document.Profile.Behaviors == null || document.Profile.Behaviors.Length == 0))
        {
            document.Profile.Behaviors = warmup.Behaviors;
            updated = true;
        }

        document.Keywords ??= new KeywordSection();
        document.Keywords.SeedList ??= new List<string>();
        document.Keywords.UsedHistory ??= new List<KeywordUsageRecord>();
        document.Keywords.KeywordHistory ??= new List<KeywordVideoRecord>();

        if (document.Keywords.SeedList.Count == 0)
        {
            var (keywords, sources) = await BuildIdentityKeywordsAsync(keywordDirectory, warmup);
            if (keywords.Count == 0)
            {
                keywords.AddRange(FallbackKeywords);
            }

            document.Keywords.SeedList = keywords;
            document.SourceFiles = sources;
            updated = true;
        }

        document.SourceFiles ??= new List<string>();
        document.Stats ??= new IdentityStats();
        document.Stats.SourceDistribution ??= new SourceBreakdown();

        _identityDocument = document;
        _identityKeywords.Clear();
        _identityKeywords.AddRange(document.Keywords.SeedList);

        if (_identityKeywords.Count == 0)
        {
            _identityKeywords.AddRange(FallbackKeywords);
            document.Keywords.SeedList.AddRange(FallbackKeywords);
            updated = true;
        }

        if (updated)
        {
            await SaveIdentityAsync();
        }
    }

    private async Task<YouTubeIdentityDocument> CreateNewIdentityDocumentAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        var (keywords, sources) = await BuildIdentityKeywordsAsync(keywordDirectory, warmup);
        if (keywords.Count == 0)
        {
            keywords.AddRange(FallbackKeywords);
        }

        var distinctKeywords = keywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profile = new IdentityProfileInfo
        {
            ProfileId = _identityKey,
            Persona = string.IsNullOrWhiteSpace(warmup.Persona) ? "Generalist" : warmup.Persona,
            Region = string.IsNullOrWhiteSpace(warmup.Region) ? "Global" : warmup.Region,
            Language = string.IsNullOrWhiteSpace(warmup.Language) ? "en-US" : warmup.Language,
            Domain = warmup.Domains?.FirstOrDefault() ?? "https://www.youtube.com",
            Timezone = warmup.Timezone ?? TimeZoneInfo.Local.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            Behaviors = warmup.Behaviors ?? Array.Empty<string>()
        };

        return new YouTubeIdentityDocument
        {
            Profile = profile,
            Keywords = new KeywordSection
            {
                SeedList = distinctKeywords,
                UsedHistory = new List<KeywordUsageRecord>(),
                KeywordHistory = new List<KeywordVideoRecord>()
            },
            Stats = new IdentityStats
            {
                LastUpdated = DateTimeOffset.UtcNow,
                SourceDistribution = new SourceBreakdown()
            },
            SourceFiles = sources
        };
    }

    private async Task<(List<string> Keywords, List<string> Sources)> BuildIdentityKeywordsAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        var keywords = new List<string>();
        var sources = new List<string>();

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
                        var fileKeywords = (await File.ReadAllLinesAsync(file))
                            .Select(line => line.Trim())
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToArray();

                        if (fileKeywords.Length > 0)
                        {
                            keywords.AddRange(fileKeywords);
                            sources.Add(Path.GetFileName(file));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to build identity keywords from directory {Directory}.", keywordDirectory);
            }
        }

        if (keywords.Count > 0)
        {
            keywords = keywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => keyword.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (keywords, sources);
    }

    private void RegisterKeywordUsage(string keyword)
    {
        if (_identityDocument?.Keywords == null)
        {
            return;
        }

        var usage = _identityDocument.Keywords.UsedHistory
            .FirstOrDefault(record => string.Equals(record.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (usage == null)
        {
            usage = new KeywordUsageRecord
            {
                Keyword = keyword,
                TimesUsed = 1,
                LastUsed = DateTimeOffset.UtcNow
            };
            _identityDocument.Keywords.UsedHistory.Add(usage);
        }
        else
        {
            usage.TimesUsed++;
            usage.LastUsed = DateTimeOffset.UtcNow;
        }
    }

    private void UpdateKeywordHistory(VideoWatchResult result)
    {
        if (_identityDocument?.Keywords == null || string.IsNullOrWhiteSpace(result.Keyword) || string.IsNullOrWhiteSpace(result.Url))
        {
            return;
        }

        var history = _identityDocument.Keywords.KeywordHistory;
        var existing = history.FirstOrDefault(item =>
            string.Equals(item.Keyword, result.Keyword, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.VideoUrl, result.Url, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            history.Add(new KeywordVideoRecord
            {
                Keyword = result.Keyword!,
                VideoUrl = result.Url!,
                WatchedMs = result.ActualWatchDurationMs,
                WatchedAt = result.StartedAt
            });
        }
        else
        {
            existing.WatchedMs = result.ActualWatchDurationMs;
            existing.WatchedAt = result.StartedAt;
        }

        const int maxHistory = 200;
        if (history.Count > maxHistory)
        {
            int remove = history.Count - maxHistory;
            history.RemoveRange(0, Math.Min(remove, history.Count));
        }
    }

    private void RecordVideoWatch(VideoWatchResult result)
    {
        string? rawTitle = string.IsNullOrWhiteSpace(result.Title) ? null : result.Title;
        string? rawChannel = string.IsNullOrWhiteSpace(result.ChannelName) ? null : result.ChannelName;
        string title = rawTitle ?? "(unknown)";
        string channel = rawChannel ?? "(unknown)";

        _logger?.LogInformation(
            "YouTube watch identity={Identity} entry={Entry} context={Context} detail={Detail} method={Method} url={Url} title={Title} channel={Channel} planned_ms={Planned} actual_ms={Actual} started={Started:o} short={IsShort} parent={Parent}.",
            _identityKey,
            result.EntryPoint,
            result.Context,
            result.ContextDetail,
            result.Method,
            result.Url ?? "unknown",
            title,
            channel,
            result.PlannedWatchDurationMs,
            result.ActualWatchDurationMs,
            result.StartedAt,
            result.IsShort,
            result.ParentVideoUrl ?? "none");

        var interaction = new SessionInteraction
        {
            Context = result.Context,
            ContextDetail = result.ContextDetail,
            EntryPoint = string.IsNullOrWhiteSpace(result.EntryPoint) ? result.Context : result.EntryPoint,
            Method = result.Method,
            ResultPosition = result.ResultPosition,
            Keyword = result.Keyword,
            VideoUrl = result.Url,
            Title = rawTitle,
            ChannelName = rawChannel,
            PlannedWatchMs = result.PlannedWatchDurationMs,
            ActualWatchMs = result.ActualWatchDurationMs,
            StartedAt = result.StartedAt,
            IsShort = result.IsShort,
            ParentVideo = result.ParentVideoUrl,
            ParentVideoTitle = result.ParentVideoTitle,
            ParentContext = result.ParentContext
        };

        _sessionInteractions.Add(interaction);
        _sessionTotalWatchTimeMs += result.ActualWatchDurationMs;

        if (IsBounce(result))
        {
            _sessionBounceCount++;
        }

        string entryPoint = interaction.EntryPoint;
        if (!_sessionSourceCounts.ContainsKey(entryPoint))
        {
            _sessionSourceCounts[entryPoint] = 0;
        }
        _sessionSourceCounts[entryPoint]++;

        UpdateKeywordHistory(result);
    }

    private void BeginSession()
    {
        _sessionInteractions.Clear();
        _sessionSourceCounts.Clear();
        _sessionTotalWatchTimeMs = 0;
        _sessionBounceCount = 0;
        _sessionStart = DateTimeOffset.UtcNow;
        _sessionId = Guid.NewGuid().ToString("N");
    }

    private async Task FinalizeSessionAsync()
    {
        if (_identityDocument == null)
        {
            return;
        }

        DateTimeOffset endedAt = DateTimeOffset.UtcNow;
        var summary = new SessionSummary
        {
            VideosWatched = _sessionInteractions.Count,
            TotalWatchTimeMs = _sessionTotalWatchTimeMs,
            AverageWatchTimeMs = _sessionInteractions.Count > 0
                ? (double)_sessionTotalWatchTimeMs / _sessionInteractions.Count
                : 0,
            BounceCount = _sessionBounceCount,
            DeepChain = _sessionInteractions.Count >= 3,
            Sources = new SessionSourceBreakdown
            {
                Search = _sessionSourceCounts.TryGetValue("Search", out var search) ? search : 0,
                Home = _sessionSourceCounts.TryGetValue("Home", out var home) ? home : 0,
                Shorts = _sessionSourceCounts.TryGetValue("Shorts", out var shorts) ? shorts : 0
            }
        };

        if (!string.IsNullOrEmpty(_sessionsDirectory))
        {
            try
            {
                Directory.CreateDirectory(_sessionsDirectory);
                var sessionLog = new SessionLog
                {
                    SessionId = _sessionId,
                    StartedAt = _sessionStart,
                    EndedAt = endedAt,
                    Interactions = new List<SessionInteraction>(_sessionInteractions),
                    Summary = summary
                };

                string fileName = $"{_sessionStart:yyyy-MM-ddTHH-mm-ss}.json";
                string sessionPath = Path.Combine(_sessionsDirectory, fileName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(sessionLog, options));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist YouTube warmup session log for identity {Identity}.", _identityKey);
            }
        }

        _logger?.LogInformation(
            "YouTube warmup session {SessionId} summary: videos={Videos} total_ms={Total} avg_ms={Average:0} bounce={Bounce} deep_chain={Deep} sources(search={Search}, home={Home}, shorts={Shorts}).",
            _sessionId,
            summary.VideosWatched,
            summary.TotalWatchTimeMs,
            summary.AverageWatchTimeMs,
            summary.BounceCount,
            summary.DeepChain,
            summary.Sources.Search,
            summary.Sources.Home,
            summary.Sources.Shorts);

        var stats = _identityDocument.Stats ??= new IdentityStats();
        stats.TotalSessions++;
        stats.TotalVideosWatched += summary.VideosWatched;
        stats.TotalWatchTimeMs += summary.TotalWatchTimeMs;
        stats.LastSessionTimestamp = endedAt;
        if (summary.VideosWatched > 0)
        {
            stats.LastWatchedAt = endedAt;
        }

        stats.AvgWatchDurationMs = stats.TotalVideosWatched > 0
            ? (double)stats.TotalWatchTimeMs / stats.TotalVideosWatched
            : 0;

        stats.BounceVideos += summary.BounceCount;
        stats.BounceRate = stats.TotalVideosWatched > 0
            ? (double)stats.BounceVideos / stats.TotalVideosWatched * 100
            : 0;

        if (summary.DeepChain)
        {
            stats.DeepSessions++;
        }
        stats.DeepSessionRate = stats.TotalSessions > 0
            ? (double)stats.DeepSessions / stats.TotalSessions * 100
            : 0;

        stats.SourceDistribution ??= new SourceBreakdown();
        stats.SourceDistribution.SearchCount += summary.Sources.Search;
        stats.SourceDistribution.HomeCount += summary.Sources.Home;
        stats.SourceDistribution.ShortsCount += summary.Sources.Shorts;

        int totalVideos = stats.TotalVideosWatched;
        if (totalVideos > 0)
        {
            stats.SourceDistribution.SearchPercent = (double)stats.SourceDistribution.SearchCount / totalVideos * 100;
            stats.SourceDistribution.HomePercent = (double)stats.SourceDistribution.HomeCount / totalVideos * 100;
            stats.SourceDistribution.ShortsPercent = (double)stats.SourceDistribution.ShortsCount / totalVideos * 100;
        }

        stats.LastUpdated = endedAt;
        _identityDocument.Profile.LastUpdated = endedAt;

        await SaveIdentityAsync();
    }

    private async Task SaveIdentityAsync()
    {
        if (_identityDocument == null || string.IsNullOrEmpty(_identityPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_identityPath)!);
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(_identityPath, JsonSerializer.Serialize(_identityDocument, options));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist YouTube identity document to {Path}.", _identityPath);
        }
    }

    private void SetActiveDomain(string domain)
    {
        if (_identityDocument?.Profile == null)
        {
            return;
        }

        if (!string.Equals(_identityDocument.Profile.Domain, domain, StringComparison.OrdinalIgnoreCase))
        {
            _identityDocument.Profile.Domain = domain;
        }
    }

    private async Task PauseBetweenActionsAsync(YouTubeWarmupSettings warmup)
    {
        int minDelay = Math.Max(500, warmup.MinDelayBetweenActionsMs);
        int maxDelay = Math.Max(minDelay, warmup.MaxDelayBetweenActionsMs);
        int pause = _random.Next(minDelay, maxDelay + 1);
        await Task.Delay(pause);
    }

    private static bool IsBounce(VideoWatchResult result)
    {
        if (result.PlannedWatchDurationMs <= 0)
        {
            return false;
        }

        double ratio = (double)result.ActualWatchDurationMs / result.PlannedWatchDurationMs;
        return ratio <= 0.2;
    }

    private static string SanitizeFileName(string identityKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(identityKey.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "identity" : sanitized;
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
