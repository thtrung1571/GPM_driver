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

    private static readonly string[] FreshLandingPhrases =
    {
        "thử tìm kiếm để bắt đầu",
        "try searching to get started"
    };

    private sealed class YouTubeIdentityDocument
    {
        public IdentityProfileInfo Profile { get; set; } = new();
        public KeywordSection Keywords { get; set; } = new();
        public IdentityStats Stats { get; set; } = new();
        public IdentityPlaybackPreferences Playback { get; set; } = new();
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
        public string VideoType { get; set; } = string.Empty;
        public int? VolumePercent { get; set; }
            = null;
    }

    private sealed class IdentityPlaybackPreferences
    {
        public int MinVolumePercent { get; set; }
            = 20;
        public int MaxVolumePercent { get; set; }
            = 80;
        public int? LastVolumePercent { get; set; }
            = null;
        public double VolumeAdjustmentChance { get; set; }
            = 0.6;
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
        public YouTubeUrlHelper.YouTubeVideoKind VideoType { get; set; }
            = YouTubeUrlHelper.YouTubeVideoKind.Unknown;
        public int? VolumePercent { get; set; }
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
        public string VideoType { get; set; } = YouTubeUrlHelper.YouTubeVideoKind.Unknown.ToString();
        public int? VolumePercent { get; set; }
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
        await EnsureOnYouTubeAsync(warmup);
        var page = await EnsurePageAsync();
        bool freshLanding = await IsFreshLandingExperienceAsync(page);
        if (freshLanding)
        {
            _logger?.LogInformation("Detected fresh YouTube landing page; prioritizing search and Shorts menu interactions.");

            if (_random.NextDouble() < 0.7)
            {
                await SearchAndWatchAsync(keywordDirectory, warmup);
            }
            else
            {
                bool opened = await OpenShortsAsync(warmup, preferGuideMenu: true);
                if (!opened)
                {
                    await SearchAndWatchAsync(keywordDirectory, warmup);
                }
            }

            return;
        }

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

    private async Task<bool> IsFreshLandingExperienceAsync(IPage page)
    {
        ILocator wrapper = page.Locator("#content-wrapper");
        if (!await wrapper.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1000 }))
        {
            return false;
        }

        string? text;
        try
        {
            text = (await wrapper.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 1000 }))?.Trim();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        foreach (string phrase in FreshLandingPhrases)
        {
            if (normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        string previousUrl = page.Url;
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

        await WaitForNavigationAsync(page, previousUrl);

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
        string beforeClickUrl = page.Url;
        await _mouseHelper!.MoveAndClickAsync(target);

        await WaitForNavigationAsync(page, beforeClickUrl);
        var result = await WatchCurrentEntryAsync(
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
        string beforeClickUrl = page.Url;
        await _mouseHelper!.MoveAndClickAsync(target);
        await WaitForNavigationAsync(page, beforeClickUrl);

        var result = await WatchCurrentEntryAsync(
            warmup,
            context: "RecommendationHome",
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

    private async Task<bool> OpenShortsAsync(YouTubeWarmupSettings warmup, bool preferGuideMenu = false)
    {
        var page = await EnsurePageAsync();
        await EnsureOnYouTubeAsync(warmup);

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
                string baseDomain = GetBaseYouTubeDomain();
                await page.GotoAsync($"{baseDomain.TrimEnd('/')}/shorts", new PageGotoOptions
                {
                    Timeout = 45000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                await Task.Delay(_random.Next(800, 1500));
                return await WatchShortsSequenceAsync(warmup);
            }

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
            return false;
        }

        int index = _random.Next(0, count);
        var target = shortsTiles.Nth(index);
        _logger?.LogInformation("Opening YouTube short #{Index} of {Total}.", index + 1, count);
        string beforeClickUrl = page.Url;
        await _mouseHelper!.MoveAndClickAsync(target);
        await WaitForNavigationAsync(page, beforeClickUrl);

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
            string previousUrl = page.Url;
            if (_random.NextDouble() < 0.55)
            {
                await page.Keyboard.PressAsync("ArrowDown");
                await WaitForNavigationAsync(page, previousUrl);
                return true;
            }

            var nextButton = page.Locator("button[aria-label*='Next'], button[aria-label*='Tiếp'], button[aria-label*='Sau']");
            if (await nextButton.CountAsync() > 0 && await nextButton.First.IsEnabledAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(nextButton.First);
                await WaitForNavigationAsync(page, previousUrl);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to advance to next short.");
        }

        return false;
    }

    private async Task<int?> MaybeAdjustVolumeAsync(IPage page)
    {
        var playback = _identityDocument?.Playback;
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
                if (_mouseHelper != null)
                {
                    await _mouseHelper.MoveAndClickAsync(videoLocator.First);
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

    private async Task<VideoWatchResult?> WatchCurrentEntryAsync(
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

    private async Task<VideoWatchResult?> WatchVideoAsync(
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
        var page = await EnsurePageAsync();
        try
        {
            await WaitForStandardPlayerAsync(page);
        }
        catch
        {
            _logger?.LogWarning("Video element did not appear for context {Context}.", context);
        }

        await FocusPlayerAsync(page, detectedKind);
        await MaybeSkipAdsAsync(page);

        int? volumePercent = await MaybeAdjustVolumeAsync(page);
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

        double detailRoll = _random.NextDouble();
        if (detailRoll < 0.25)
        {
            var showMore = await TryGetFirstVisibleLocatorAsync(
                page,
                "tp-yt-paper-button#expand",
                "ytd-text-inline-expander tp-yt-paper-button#expand",
                "ytd-expander tp-yt-paper-button#more");
            if (showMore != null)
            {
                if (_mouseHelper != null)
                {
                    await _mouseHelper.MoveAndClickAsync(showMore);
                }
                else
                {
                    await showMore.ClickAsync();
                }
            }
        }
        else if (detailRoll < 0.35)
        {
            var collapse = await TryGetFirstVisibleLocatorAsync(
                page,
                "tp-yt-paper-button#collapse",
                "ytd-expander tp-yt-paper-button#collapse");
            if (collapse != null)
            {
                if (_mouseHelper != null)
                {
                    await _mouseHelper.MoveAndClickAsync(collapse);
                }
                else
                {
                    await collapse.ClickAsync();
                }
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
            ParentContext = parent?.ContextDetail ?? parent?.Context,
            VolumePercent = volumePercent
        };

        result.VideoType = YouTubeUrlHelper.GetVideoKind(result.Url);
        result.IsShort = result.VideoType == YouTubeUrlHelper.YouTubeVideoKind.Short;

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
        await WaitForShortPlayerAsync(page);
        await FocusPlayerAsync(page, YouTubeUrlHelper.YouTubeVideoKind.Short);
        await MaybeSkipAdsAsync(page);

        int? volumePercent = await MaybeAdjustVolumeAsync(page);
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
            ParentContext = parent?.ContextDetail ?? parent?.Context,
            VolumePercent = volumePercent
        };

        result.VideoType = YouTubeUrlHelper.GetVideoKind(result.Url);
        result.IsShort = result.VideoType == YouTubeUrlHelper.YouTubeVideoKind.Short;

        RecordVideoWatch(result);
        return result;
    }

    private async Task WaitForNavigationAsync(IPage page, string previousUrl)
    {
        try
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (!string.Equals(page.Url, previousUrl, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(250);
            }

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Task.Delay(_random.Next(900, 1800));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Navigation wait encountered an issue.");
        }
    }

    private static async Task WaitForStandardPlayerAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync(
                "video.html5-main-video, ytd-player video",
                new PageWaitForSelectorOptions { Timeout = 20000 });
        }
        catch
        {
            // ignore and let caller handle missing player
        }
    }

    private static async Task WaitForShortPlayerAsync(IPage page)
    {
        try
        {
            await page.WaitForSelectorAsync(
                "#shorts-player video, ytd-reel-video-renderer video, ytd-shorts-player-renderer video",
                new PageWaitForSelectorOptions { Timeout = 20000 });
        }
        catch
        {
            // best effort
        }
    }

    private async Task FocusPlayerAsync(IPage page, YouTubeUrlHelper.YouTubeVideoKind kind)
    {
        try
        {
            string[] selectors = kind == YouTubeUrlHelper.YouTubeVideoKind.Short
                ? new[]
                {
                    "#shorts-player video",
                    "#shorts-player",
                    "ytd-reel-video-renderer video"
                }
                : new[]
                {
                    "video.html5-main-video",
                    "ytd-player video",
                    "#movie_player"
                };

            foreach (string selector in selectors)
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() == 0)
                {
                    continue;
                }

                var element = locator.First;
                if (!await element.IsVisibleAsync())
                {
                    continue;
                }

                await element.FocusAsync();
                if (_mouseHelper != null)
                {
                    await _mouseHelper.MoveToAsync(element, steps: 12, addJitter: true);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to focus YouTube player.");
        }
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
            if (parent.VideoType == YouTubeUrlHelper.YouTubeVideoKind.Short)
            {
                if (!await TryNavigateShortContinuationAsync(page))
                {
                    break;
                }

                string beforeShortUrl = parent.Url ?? page.Url;
                await WaitForNavigationAsync(page, beforeShortUrl);

                var shortResult = await WatchCurrentEntryAsync(
                    warmup,
                    context: "RecommendationNext",
                    method: "ShortsNavigation",
                    contextDetail: $"ShortsChain#{i + 1}",
                    entryPoint: parent.EntryPoint ?? "Recommendation",
                    keyword: null,
                    position: null,
                    parent: parent);

                if (shortResult == null)
                {
                    break;
                }

                parent = shortResult;
                continue;
            }

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

            await WaitForNavigationAsync(page, previousUrl);

            if (string.Equals(previousUrl, page.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Recommended navigation did not change URL. Ending chain early.");
                break;
            }

            string recommendationContext = "RecommendationNext";

            var result = await WatchCurrentEntryAsync(
                warmup,
                context: recommendationContext,
                method: method,
                contextDetail: contextDetail,
                entryPoint: parent.EntryPoint ?? "Recommendation",
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

    private async Task<bool> TryNavigateShortContinuationAsync(IPage page)
    {
        try
        {
            string previousUrl = page.Url;
            var downButton = page.Locator("#navigation-button-down button, #navigation-button-down tp-yt-paper-button, #navigation-button-down");
            if (await downButton.CountAsync() > 0 && await downButton.First.IsVisibleAsync())
            {
                await _mouseHelper!.MoveAndClickAsync(downButton.First);
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

    private async Task MaybeSkipAdsAsync(IPage page)
    {
        try
        {
            bool adDetected = false;
            int? detectedDurationMs = null;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                adDetected = await page.EvaluateAsync<bool>(
                    "() => document.querySelector('.ad-showing, .ytp-ad-player-overlay, .ytp-ad-overlay-slot') !== null");
                if (adDetected)
                {
                    string? durationText = await page.EvaluateAsync<string?>(
                        "() => document.querySelector('.ytp-ad-duration-remaining .ytp-time-duration, .ytp-ad-duration-remaining')?.textContent ?? null");
                    detectedDurationMs = ParseDurationToMilliseconds(durationText);
                    break;
                }

                await Task.Delay(350);
            }

            if (!adDetected)
            {
                return;
            }

            int dwellMs;
            if (detectedDurationMs.HasValue && detectedDurationMs.Value <= 15000)
            {
                dwellMs = _random.Next(5000, 8000);
            }
            else if (detectedDurationMs.HasValue && detectedDurationMs.Value <= 30000)
            {
                dwellMs = _random.Next(5000, 15000);
            }
            else
            {
                dwellMs = _random.Next(6000, 16000);
            }

            await Task.Delay(dwellMs);

            var skipSelectors = new[]
            {
                "button.ytp-ad-skip-button", // primary skip button
                ".ytp-ad-skip-button.ytp-button", // legacy skip button style
                "button.ytp-ad-skip-button-modern", // new UI variant
                ".ytp-ad-overlay-close-button", // overlay ads
                "button[aria-label*='Skip'], button[aria-label*='skip']",
                "#skip-button .ytp-button"
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
                    if (_mouseHelper != null)
                    {
                        await _mouseHelper.MoveAndClickAsync(target);
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
        if (document.Playback == null)
        {
            document.Playback = CreatePlaybackPreferences();
            updated = true;
        }
        if (document.Playback.MinVolumePercent < 0 || document.Playback.MinVolumePercent > 100)
        {
            document.Playback.MinVolumePercent = 20;
            updated = true;
        }
        if (document.Playback.MaxVolumePercent < 0 || document.Playback.MaxVolumePercent > 100)
        {
            document.Playback.MaxVolumePercent = 80;
            updated = true;
        }
        if (document.Playback.MinVolumePercent > document.Playback.MaxVolumePercent)
        {
            (document.Playback.MinVolumePercent, document.Playback.MaxVolumePercent) = (document.Playback.MaxVolumePercent, document.Playback.MinVolumePercent);
            updated = true;
        }
        if (document.Playback.VolumeAdjustmentChance <= 0 || document.Playback.VolumeAdjustmentChance > 1)
        {
            document.Playback.VolumeAdjustmentChance = 0.6;
            updated = true;
        }
        else
        {
            double clampedChance = Math.Clamp(document.Playback.VolumeAdjustmentChance, 0, 1);
            if (!document.Playback.VolumeAdjustmentChance.Equals(clampedChance))
            {
                document.Playback.VolumeAdjustmentChance = clampedChance;
                updated = true;
            }
        }

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

    private IdentityPlaybackPreferences CreatePlaybackPreferences()
    {
        int min = _random.Next(10, 41);
        int maxLowerBound = Math.Max(min + 15, 55);
        maxLowerBound = Math.Min(maxLowerBound, 95);
        int max = _random.Next(maxLowerBound, 101);
        if (max < min)
        {
            max = Math.Min(90, min + 10);
        }

        double chance = 0.45 + _random.NextDouble() * 0.35;

        return new IdentityPlaybackPreferences
        {
            MinVolumePercent = Math.Clamp(min, 0, 100),
            MaxVolumePercent = Math.Clamp(Math.Max(max, min + 5), 0, 100),
            VolumeAdjustmentChance = Math.Clamp(chance, 0.1, 0.95)
        };
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
            Playback = CreatePlaybackPreferences(),
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
                WatchedAt = result.StartedAt,
                VideoType = result.VideoType.ToString(),
                VolumePercent = result.VolumePercent
            });
        }
        else
        {
            existing.WatchedMs = result.ActualWatchDurationMs;
            existing.WatchedAt = result.StartedAt;
            existing.VideoType = result.VideoType.ToString();
            existing.VolumePercent = result.VolumePercent;
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
            "YouTube watch identity={Identity} entry={Entry} context={Context} detail={Detail} method={Method} url={Url} title={Title} channel={Channel} type={Type} planned_ms={Planned} actual_ms={Actual} started={Started:o} short={IsShort} volume={Volume} parent={Parent}.",
            _identityKey,
            result.EntryPoint,
            result.Context,
            result.ContextDetail,
            result.Method,
            result.Url ?? "unknown",
            title,
            channel,
            result.VideoType,
            result.PlannedWatchDurationMs,
            result.ActualWatchDurationMs,
            result.StartedAt,
            result.IsShort,
            result.VolumePercent,
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
            ParentContext = result.ParentContext,
            VideoType = result.VideoType.ToString(),
            VolumePercent = result.VolumePercent
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

        if (result.VolumePercent.HasValue && _identityDocument?.Playback != null)
        {
            _identityDocument.Playback.LastVolumePercent = Math.Clamp(result.VolumePercent.Value, 0, 100);
        }

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

    private async Task<bool> TryEnsureGuideMenuVisibleAsync(IPage page)
    {
        var miniGuide = page.Locator("#items ytd-mini-guide-entry-renderer");
        if (await miniGuide.CountAsync() > 0)
        {
            var firstItem = miniGuide.First;
            if (await firstItem.IsVisibleAsync())
            {
                return false;
            }
        }

        var menuButton = page.Locator("ytd-masthead button#guide-button").First;
        if (!await menuButton.IsVisibleAsync())
        {
            return false;
        }

        if (_mouseHelper != null)
        {
            await _mouseHelper.MoveAndClickAsync(menuButton);
        }
        else
        {
            await menuButton.ClickAsync();
        }

        await Task.Delay(_random.Next(600, 1200));
        return true;
    }

    private async Task<ILocator?> TryGetFirstVisibleLocatorAsync(IPage page, params string[] selectors)
    {
        foreach (string selector in selectors)
        {
            var locator = page.Locator(selector);
            int count;
            try
            {
                count = await locator.CountAsync();
            }
            catch
            {
                continue;
            }

            if (count == 0)
            {
                continue;
            }

            var candidate = locator.First;
            if (await candidate.IsVisibleAsync())
            {
                return candidate;
            }
        }

        return null;
    }

    private Task<ILocator?> TryFindShortsLinkAsync(IPage page)
    {
        return TryGetFirstVisibleLocatorAsync(
            page,
            "#items ytd-mini-guide-entry-renderer:nth-child(2) a",
            "ytd-mini-guide-entry-renderer a[href*='shorts']",
            "a[title='Shorts']",
            "a[href='/shorts']");
    }

    private string GetBaseYouTubeDomain()
    {
        string? domain = _identityDocument?.Profile?.Domain;
        if (!string.IsNullOrWhiteSpace(domain) && Uri.TryCreate(domain, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return "https://www.youtube.com";
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
