using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services.YouTube;
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
    private int _sessionFailureCount;

    private VideoPlayerService? _videoPlayer;
    private RecommendationChainService? _recommendationService;
    private SearchInteractionService? _searchService;
    private ShortsInteractionService? _shortsService;
    private HomeInteractionService? _homeService;

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

    internal sealed class YouTubeIdentityDocument
    {
        public IdentityProfileInfo Profile { get; set; } = new();
        public KeywordSection Keywords { get; set; } = new();
        public IdentityStats Stats { get; set; } = new();
        public IdentityPlaybackPreferences Playback { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
    }

    internal sealed class IdentityProfileInfo
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

    internal sealed class KeywordSection
    {
        public List<string> SeedList { get; set; } = new();
        public List<KeywordUsageRecord> UsedHistory { get; set; } = new();
        public List<KeywordVideoRecord> KeywordHistory { get; set; } = new();
    }

    internal sealed class KeywordUsageRecord
    {
        public string Keyword { get; set; } = string.Empty;
        public int TimesUsed { get; set; }
            = 0;
        public DateTimeOffset? LastUsed { get; set; }
            = null;
    }

    internal sealed class KeywordVideoRecord
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

    internal sealed class IdentityPlaybackPreferences
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

    internal sealed class IdentityStats
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
        public int FailedInteractions { get; set; }
            = 0;
        public double ErrorRate { get; set; }
            = 0;
        public DateTimeOffset? LastSessionTimestamp { get; set; }
            = null;
        public DateTimeOffset? LastWatchedAt { get; set; }
            = null;
        public SourceBreakdown SourceDistribution { get; set; } = new();
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class SourceBreakdown
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
        public int FailedInteractions { get; set; }
            = 0;
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
            int attempts = 0;
            while (true)
            {
                try
                {
                    await EnsureOnYouTubeAsync(warmup);
                    attempts++;
                    bool success = await PerformInteractionAsync(keywordDirectory, warmup);
                    if (success)
                    {
                        completed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "YouTube interaction failed.");
                    RegisterInteractionFailure();
                }

                _isFirstRun = false;

                if (attempts >= 2 && _sessionFailureCount > attempts / 2)
                {
                    _logger?.LogWarning("Ending YouTube warmup early due to elevated failure rate (failures={Failures} attempts={Attempts}).", _sessionFailureCount, attempts);
                    break;
                }

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

    private async Task<bool> PerformInteractionAsync(string? keywordDirectory, YouTubeWarmupSettings warmup)
    {
        await EnsureOnYouTubeAsync(warmup);
        var page = await EnsurePageAsync();
        EnsureInteractionServices();

        bool freshLanding = await IsFreshLandingExperienceAsync(page);
        bool success;

        if (freshLanding)
        {
            _logger?.LogInformation("Detected fresh YouTube landing page; prioritizing search and Shorts menu interactions.");

            if (_random.NextDouble() < 0.7)
            {
                success = await _searchService!.ExecuteAsync(keywordDirectory, warmup);
            }
            else
            {
                success = await _shortsService!.ExecuteAsync(warmup, preferGuideMenu: true);
                if (!success)
                {
                    success = await _searchService!.ExecuteAsync(keywordDirectory, warmup);
                }
            }

            if (!success)
            {
                RegisterInteractionFailure();
            }

            return success;
        }

        double normalizedSearchWeight = Math.Clamp(warmup.SearchWeight, 0, 1);
        double normalizedShortsWeight = Math.Clamp(warmup.ShortsWeight, 0, 1 - normalizedSearchWeight);

        double roll = _random.NextDouble();
        if (_isFirstRun)
        {
            roll = Math.Min(roll, normalizedSearchWeight + 0.1);
        }

        if (roll < normalizedSearchWeight)
        {
            _logger?.LogInformation("YouTube warmup selected search interaction.");
            success = await _searchService!.ExecuteAsync(keywordDirectory, warmup);
        }
        else if (roll < normalizedSearchWeight + normalizedShortsWeight)
        {
            _logger?.LogInformation("YouTube warmup selected Shorts interaction.");
            success = await _shortsService!.ExecuteAsync(warmup, preferGuideMenu: false);
            if (!success)
            {
                _logger?.LogInformation("Shorts interaction unavailable; falling back to home recommendation.");
                success = await _homeService!.ExecuteAsync(warmup);
            }
        }
        else
        {
            _logger?.LogInformation("YouTube warmup selected home recommendation interaction.");
            success = await _homeService!.ExecuteAsync(warmup);
        }

        if (!success)
        {
            RegisterInteractionFailure();
        }

        return success;
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

    private void EnsureInteractionServices()
    {
        _videoPlayer ??= new VideoPlayerService(
            EnsurePageAsync,
            () => _mouseHelper,
            () => _keyboardHelper,
            _random,
            _logger,
            GetPlaybackPreferences,
            RecordVideoWatch,
            PauseAfterPlaybackAsync);

        _recommendationService ??= new RecommendationChainService(
            EnsurePageAsync,
            () => _mouseHelper,
            _random,
            _logger,
            _videoPlayer,
            WaitForNavigationAsync);

        _homeService ??= new HomeInteractionService(
            EnsurePageAsync,
            NavigateHomeAsync,
            _videoPlayer,
            _recommendationService,
            WaitForNavigationAsync,
            _random,
            () => _mouseHelper,
            _logger);

        _shortsService ??= new ShortsInteractionService(
            EnsurePageAsync,
            EnsureOnYouTubeAsync,
            GetBaseYouTubeDomain,
            WaitForNavigationAsync,
            _videoPlayer,
            _random,
            () => _mouseHelper,
            _logger);

        _searchService ??= new SearchInteractionService(
            EnsurePageAsync,
            EnsureOnYouTubeAsync,
            async directory => await PickKeywordAsync(directory),
            RegisterKeywordUsage,
            FocusSearchBoxAsync,
            ClearInputAsync,
            WaitForNavigationAsync,
            _videoPlayer,
            _recommendationService,
            _random,
            () => _mouseHelper,
            () => _keyboardHelper,
            _logger,
            async settings => await _homeService!.ExecuteAsync(settings));
    }

    private Task PauseAfterPlaybackAsync() => Task.Delay(_random.Next(400, 900));

    private YouTubeWarmupService.IdentityPlaybackPreferences? GetPlaybackPreferences()
        => _identityDocument?.Playback;

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

    private void BeginSession()
    {
        _sessionInteractions.Clear();
        _sessionSourceCounts.Clear();
        _sessionTotalWatchTimeMs = 0;
        _sessionBounceCount = 0;
        _sessionFailureCount = 0;
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
            FailedInteractions = _sessionFailureCount,
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
            "YouTube warmup session {SessionId} summary: videos={Videos} total_ms={Total} avg_ms={Average:0} bounce={Bounce} deep_chain={Deep} fails={Fails} sources(search={Search}, home={Home}, shorts={Shorts}).",
            _sessionId,
            summary.VideosWatched,
            summary.TotalWatchTimeMs,
            summary.AverageWatchTimeMs,
            summary.BounceCount,
            summary.DeepChain,
            summary.FailedInteractions,
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

        int denominator = stats.TotalVideosWatched + Math.Max(0, stats.FailedInteractions);
        stats.ErrorRate = denominator > 0
            ? (double)stats.FailedInteractions / denominator * 100
            : 0;

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
