using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services.YouTube;
using GPM_driver.Services.YouTube.Activities;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube;

internal sealed class WarmupContext
{
    private readonly ILogger<WarmupContext>? _logger;
    private readonly ILoggerFactory? _loggerFactory;

    private PlayerControlHelper? _playerControls;
    private VideoPlayerActivity? _videoActivity;

    private int _searchInteractions;
    private int _homeInteractions;
    private int _shortsInteractions;
    private int _recommendationInteractions;

    public WarmupContext(
        IBrowserContext browserContext,
        ILogger<WarmupContext>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        BrowserContext = browserContext ?? throw new ArgumentNullException(nameof(browserContext));
        _logger = logger;
        _loggerFactory = loggerFactory;
        Random = RandomProvider.Shared;
    }

    public IBrowserContext BrowserContext { get; }

    public Random Random { get; }

    public string? KeywordDirectory { get; set; }

    public IPage? Page { get; private set; }

    public MouseHelper? Mouse { get; private set; }

    public KeyboardHelper? Keyboard { get; private set; }

    public HashSet<string> VisitedVideoIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PlayerControlHelper PlayerControls
    {
        get
        {
            if (_playerControls == null && Page != null)
            {
                _playerControls = new PlayerControlHelper(
                    Page,
                    Mouse,
                    _loggerFactory?.CreateLogger<PlayerControlHelper>());
            }

            return _playerControls ?? throw new InvalidOperationException("Warmup context is not initialised.");
        }
    }

    public VideoPlayerActivity VideoActivity
    {
        get
        {
            if (_videoActivity == null && Page != null)
            {
                _videoActivity = new VideoPlayerActivity(
                    Page,
                    _loggerFactory?.CreateLogger<VideoPlayerActivity>(),
                    PlayerControls);
            }

            return _videoActivity ?? throw new InvalidOperationException("Warmup context is not initialised.");
        }
    }

    public int SearchInteractions => _searchInteractions;

    public int HomeInteractions => _homeInteractions;

    public int ShortsInteractions => _shortsInteractions;

    public int RecommendationInteractions => _recommendationInteractions;

    public async Task<IPage> EnsurePageAsync(CancellationToken token)
    {
        if (Page != null && !Page.IsClosed)
        {
            return Page;
        }

        var existing = BrowserContext.Pages.FirstOrDefault(static p => !p.IsClosed);
        if (existing != null)
        {
            Page = existing;
        }
        else
        {
            _logger?.LogDebug("Opening new page for YouTube warmup context.");
            Page = await BrowserContext.NewPageAsync();
        }

        Mouse = new MouseHelper(Page);
        Keyboard = new KeyboardHelper(Page);
        _playerControls = null;
        _videoActivity = null;

        return Page;
    }

    public async Task EnsureLandingDomainAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (Page == null)
        {
            return;
        }

        string[] domains = config.Domains?.Length > 0
            ? config.Domains
            : new[] { "https://www.youtube.com" };

        if (domains.Any(domain => Page.Url.StartsWith(domain, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string target = domains[Random.Next(domains.Length)];
        _logger?.LogInformation("Navigating to YouTube domain {Domain} to begin warmup.", target);
        await Page.GotoAsync(target, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 30000 });
    }

    public async Task<bool> IsFreshLandingExperienceAsync(CancellationToken token)
    {
        if (Page == null)
        {
            return false;
        }

        try
        {
            var contentWrapper = Page.Locator("#content-wrapper");
            if (!await contentWrapper.IsVisibleAsync(new() { Timeout = 1500 }))
            {
                return false;
            }

            string text = (await contentWrapper.InnerTextAsync(new() { Timeout = 1500 })) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] markers =
            {
                "thử tìm kiếm để bắt đầu",
                "try searching to get started"
            };

            return markers.Any(marker =>
                text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task NavigateToHomeAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (Page == null)
        {
            return;
        }

        if (Page.Url.StartsWith("https://www.youtube.com/", StringComparison.OrdinalIgnoreCase) &&
            !Page.Url.Contains("watch", StringComparison.OrdinalIgnoreCase) &&
            !Page.Url.Contains("results", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string home = config.Domains?.FirstOrDefault(d =>
            d.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !d.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
            ?? "https://www.youtube.com";

        await Page.GotoAsync(home, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 20000 });
    }

    public async Task WaitForNavigationAsync(CancellationToken token)
    {
        if (Page == null)
        {
            return;
        }

        try
        {
            await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 30000 });
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Wait for navigation timed out but continuing warmup.");
        }
    }

    public async Task WatchVideoAsync(
        YouTubeWarmupSettings config,
        CancellationToken token,
        TimeSpan? explicitDuration = null,
        bool isShort = false)
    {
        if (Page == null)
        {
            return;
        }

        int min = isShort ? config.MinShortWatchMilliseconds : config.MinWatchMilliseconds;
        int max = isShort ? config.MaxShortWatchMilliseconds : config.MaxWatchMilliseconds;

        if (max < min)
        {
            max = min;
        }

        TimeSpan duration = explicitDuration ?? TimeSpan.FromMilliseconds(
            Random.Next(Math.Max(1000, min), Math.Max(1000, max) + 1));

        var activity = VideoActivity;
        activity.MinDelayBetweenActionsMs = Math.Max(1200, config.MinDelayBetweenActionsMs);
        activity.MaxDelayBetweenActionsMs = Math.Max(activity.MinDelayBetweenActionsMs + 1000, config.MaxDelayBetweenActionsMs);

        try
        {
            await activity.WatchCurrentVideoAsync(duration, token, isShort ? true : null);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Watch routine encountered an issue.");
        }

        TrackVisitedVideo(Page.Url);
    }

    public async Task DelayBetweenActionsAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        int minDelay = Math.Max(0, config.MinDelayBetweenActionsMs);
        int maxDelay = Math.Max(minDelay, config.MaxDelayBetweenActionsMs);
        int delay = Random.Next(minDelay, maxDelay + 1);
        await Task.Delay(delay, token);
    }

    public void IncrementSearchInteractions() => _searchInteractions++;

    public void IncrementHomeInteractions() => _homeInteractions++;

    public void IncrementShortsInteractions() => _shortsInteractions++;

    public void IncrementRecommendationInteractions() => _recommendationInteractions++;

    public async Task PersistSessionStateAsync(YouTubeWarmupSettings config, string? profileId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(config.IdentityCacheDirectory))
        {
            return;
        }

        try
        {
            string directory = config.IdentityCacheDirectory;
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(AppContext.BaseDirectory, directory);
            }

            Directory.CreateDirectory(directory);

            string fileName = string.IsNullOrWhiteSpace(profileId)
                ? "youtube-warmup-session.json"
                : $"youtube-warmup-session-{SanitizeFileName(profileId)}.json";

            var payload = new
            {
                ProfileId = profileId,
                TimestampUtc = DateTime.UtcNow,
                SearchInteractions = _searchInteractions,
                HomeInteractions = _homeInteractions,
                ShortsInteractions = _shortsInteractions,
                RecommendationInteractions = _recommendationInteractions,
                VisitedVideoIds = VisitedVideoIds.ToArray()
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            string path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, json, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Unable to persist YouTube warmup identity cache.");
        }
    }

    public string ExtractVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (uri.AbsolutePath.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
        {
            string segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            return segment;
        }

        if (string.IsNullOrEmpty(uri.Query))
        {
            return string.Empty;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (kv.Length == 0)
            {
                continue;
            }

            if (kv[0].Equals("v", StringComparison.OrdinalIgnoreCase))
            {
                return kv.Length == 2 ? kv[1] : string.Empty;
            }
        }

        return string.Empty;
    }

    private void TrackVisitedVideo(string? url)
    {
        string id = ExtractVideoId(url);
        if (!string.IsNullOrEmpty(id))
        {
            VisitedVideoIds.Add(id);
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
