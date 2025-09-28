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

namespace GPM_driver.Services;

internal class YouTubeWarmupService
{
    private readonly IBrowserContext _context;
    private readonly ILogger<YouTubeWarmupService>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    private IPage? _page;
    private MouseHelper? _mouseHelper;
    private KeyboardHelper? _keyboardHelper;
    private readonly HashSet<string> _visitedVideoIds = new(StringComparer.OrdinalIgnoreCase);

    private int _searchInteractions;
    private int _homeInteractions;
    private int _shortsInteractions;
    private int _recommendationInteractions;
    private List<string>? _cachedKeywords;


    public YouTubeWarmupService(IBrowserContext context, ILogger<YouTubeWarmupService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task RunWarmupAsync(
        string? keywordDirectory,
        YouTubeWarmupSettings warmupConfig,
        string? profileId,
        CancellationToken token = default)
    {
        if (warmupConfig == null)
        {
            throw new ArgumentNullException(nameof(warmupConfig));
        }

        _page = await EnsurePrimaryPageAsync(token);
        _mouseHelper = new MouseHelper(_page);
        _keyboardHelper = new KeyboardHelper(_page);

        await EnsureLandingDomainAsync(warmupConfig, token);

        bool freshLanding = await IsFreshLandingExperienceAsync(token);
        _logger?.LogInformation(
            freshLanding
                ? "Detected fresh YouTube landing experience. Prioritising search and Shorts warmup."
                : "YouTube landing shows personalised feed. Running full warmup mix.");

        int interactions = _random.Next(
            Math.Max(1, warmupConfig.MinInteractions),
            Math.Max(Math.Max(1, warmupConfig.MinInteractions), warmupConfig.MaxInteractions) + 1);

        for (int i = 0; i < interactions && !token.IsCancellationRequested; i++)
        {
            var behaviour = SelectBehaviour(warmupConfig, freshLanding);

            _logger?.LogDebug("Executing YouTube warmup behaviour {Behaviour} ({Index}/{Total}).", behaviour, i + 1, interactions);

            bool success = behaviour switch
            {
                WarmupBehaviour.Search => await RunSearchInteractionAsync(keywordDirectory, warmupConfig, token),
                WarmupBehaviour.Home => await RunHomeFeedInteractionAsync(warmupConfig, token),
                WarmupBehaviour.Shorts => await RunShortsSequenceAsync(warmupConfig, token),
                WarmupBehaviour.Recommendations => await RunRecommendationChainAsync(warmupConfig, token),
                _ => false
            };

            if (!success)
            {
                _logger?.LogDebug("Behaviour {Behaviour} did not complete successfully; continuing with next action.", behaviour);
            }

            if (i < interactions - 1)
            {
                int delay = _random.Next(warmupConfig.MinDelayBetweenActionsMs, warmupConfig.MaxDelayBetweenActionsMs + 1);
                await Task.Delay(delay, token);
            }
        }

        await PersistSessionStateAsync(warmupConfig, profileId, token);

        _logger?.LogInformation(
            "YouTube warmup finished with {Search} searches, {Home} home plays, {Shorts} shorts sequences, {Recommendations} recommendation hops. Visited {Videos} unique videos.",
            _searchInteractions,
            _homeInteractions,
            _shortsInteractions,
            _recommendationInteractions,
            _visitedVideoIds.Count);
    }

    public async Task<bool> IsFreshLandingExperienceAsync(CancellationToken token = default)
    {
        if (_page == null)
        {
            return false;
        }

        try
        {
            var contentWrapper = _page.Locator("#content-wrapper");
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

    private enum WarmupBehaviour
    {
        Search,
        Home,
        Shorts,
        Recommendations
    }

    private WarmupBehaviour SelectBehaviour(YouTubeWarmupSettings config, bool freshLanding)
    {
        var behaviours = config.Behaviors?.Length > 0
            ? config.Behaviors
            : new[] { "Search", "Home", "Shorts", "Recommendations" };

        var weighted = behaviours
            .Select(name => (name, behaviour: ParseBehaviour(name)))
            .Where(tuple => tuple.behaviour != null)
            .Select(tuple => tuple.behaviour!.Value)
            .Distinct()
            .ToList();

        if (weighted.Count == 0)
        {
            weighted.Add(WarmupBehaviour.Search);
        }

        double searchWeight = freshLanding ? 0.7 : Math.Max(0.05, config.SearchWeight);
        double shortsWeight = freshLanding ? 0.3 : Math.Max(0.01, config.ShortsWeight);
        double homeWeight = freshLanding ? 0.0 : Math.Max(0.01, config.HomeWeight);
        double recommendationWeight = freshLanding ? 0.0 : Math.Max(0.01, config.RecommendationsWeight);

        double total = 0;
        var bag = new List<(WarmupBehaviour Behaviour, double Weight)>();

        foreach (var behaviour in weighted)
        {
            double weight = behaviour switch
            {
                WarmupBehaviour.Search => searchWeight,
                WarmupBehaviour.Shorts => shortsWeight,
                WarmupBehaviour.Home => homeWeight,
                WarmupBehaviour.Recommendations => recommendationWeight,
                _ => 0.0
            };

            if (weight <= 0)
            {
                continue;
            }

            bag.Add((behaviour, weight));
            total += weight;
        }

        if (bag.Count == 0)
        {
            bag.Add((WarmupBehaviour.Search, 1.0));
            total = 1.0;
        }

        double roll = _random.NextDouble() * total;
        double cumulative = 0;
        foreach (var entry in bag)
        {
            cumulative += entry.Weight;
            if (roll <= cumulative)
            {
                return entry.Behaviour;
            }
        }

        return bag.Last().Behaviour;
    }

    private WarmupBehaviour? ParseBehaviour(string behaviour)
    {
        if (string.IsNullOrWhiteSpace(behaviour))
        {
            return null;
        }

        return behaviour.Trim().ToLowerInvariant() switch
        {
            "search" => WarmupBehaviour.Search,
            "home" => WarmupBehaviour.Home,
            "short" or "shorts" => WarmupBehaviour.Shorts,
            "recommendation" or "recommendations" => WarmupBehaviour.Recommendations,
            _ => null
        };
    }

    private async Task<IPage> EnsurePrimaryPageAsync(CancellationToken token)
    {
        var page = _page;
        if (page != null && !page.IsClosed)
        {
            return page;
        }

        page = _context.Pages.FirstOrDefault(p => !p.IsClosed);
        if (page != null)
        {
            _page = page;
            return page;
        }

        _logger?.LogDebug("Opening new page for YouTube warmup.");
        page = await _context.NewPageAsync();
        _page = page;
        return page;
    }

    private async Task EnsureLandingDomainAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null)
        {
            return;
        }

        string[] domains = config.Domains?.Length > 0
            ? config.Domains
            : new[] { "https://www.youtube.com" };

        if (!domains.Any(domain => _page.Url.StartsWith(domain, StringComparison.OrdinalIgnoreCase)))
        {
            string target = domains[_random.Next(domains.Length)];
            _logger?.LogInformation("Navigating to YouTube domain {Domain} to begin warmup.", target);
            await _page.GotoAsync(target, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 30000 });
        }
    }

    private async Task<bool> RunSearchInteractionAsync(string? keywordDirectory, YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null || _mouseHelper == null || _keyboardHelper == null)
        {
            return false;
        }

        string keyword = await GetRandomKeywordAsync(keywordDirectory, token);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        await NavigateToHomeAsync(config, token);

        var searchInput = _page.Locator("input#search");
        if (!await searchInput.IsVisibleAsync(new() { Timeout = 3000 }))
        {
            _logger?.LogDebug("Search input not visible; attempting to open search box.");
            await _page.Keyboard.PressAsync("/");
            await Task.Delay(_random.Next(200, 450), token);
        }

        if (!await searchInput.IsVisibleAsync(new() { Timeout = 3000 }))
        {
            _logger?.LogWarning("Unable to locate YouTube search box.");
            return false;
        }

        await _mouseHelper.MoveAndClickAsync(searchInput);
        await _page.Keyboard.PressAsync("Control+A");
        await Task.Delay(_random.Next(100, 250), token);
        await _page.Keyboard.PressAsync("Backspace");

        await _keyboardHelper.TypeLikeHumanAsync(searchInput, keyword);

        if (_random.NextDouble() < 0.35)
        {
            await Task.Delay(_random.Next(400, 900), token);
            int arrowPresses = _random.Next(1, 3);
            await _keyboardHelper.PressNavigationKeyAsync("ArrowDown", arrowPresses);
        }

        if (_random.NextDouble() < 0.2)
        {
            var searchButton = _page.Locator("button#search-icon-legacy");
            if (await searchButton.IsVisibleAsync(new() { Timeout = 1500 }))
            {
                await _mouseHelper.MoveAndClickAsync(searchButton);
            }
            else
            {
                await _page.Keyboard.PressAsync("Enter");
            }
        }
        else
        {
            await _page.Keyboard.PressAsync("Enter");
        }

        await WaitForNavigationAsync(token);

        var firstResult = _page.Locator("ytd-video-renderer a#thumbnail, ytd-video-renderer #video-title-link").First;
        if (!await firstResult.IsVisibleAsync(new() { Timeout = 7000 }))
        {
            _logger?.LogWarning("No visible search results located for keyword '{Keyword}'.", keyword);
            return false;
        }

        int index = _random.Next(0, Math.Min(5, await _page.Locator("ytd-video-renderer").CountAsync()));
        var target = _page.Locator("ytd-video-renderer a#thumbnail, ytd-video-renderer #video-title-link").Nth(index);

        try
        {
            await _mouseHelper.MoveAndClickAsync(target);
            await WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed to open search result.");
            return false;
        }

        await WatchCurrentVideoAsync(config, token);
        _searchInteractions++;
        return true;
    }

    private async Task<bool> RunHomeFeedInteractionAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null || _mouseHelper == null)
        {
            return false;
        }

        await NavigateToHomeAsync(config, token);

        var items = _page.Locator("ytd-rich-grid-row ytd-rich-item-renderer a#thumbnail, ytd-rich-item-renderer #video-title-link");
        int count = await items.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("Home feed did not return any clickable videos.");
            return false;
        }

        int index = _random.Next(0, Math.Min(count, 8));
        var item = items.Nth(index);
        try
        {
            await _mouseHelper.MoveAndClickAsync(item);
            await WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed clicking home feed item {Index}.", index);
            return false;
        }

        await WatchCurrentVideoAsync(config, token);
        _homeInteractions++;
        return true;
    }

    private async Task<bool> RunShortsSequenceAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null || _mouseHelper == null)
        {
            return false;
        }

        bool navigated = await TryOpenShortsFeedAsync(config, token);
        if (!navigated)
        {
            return false;
        }

        int sequenceLength = _random.Next(
            Math.Max(1, config.MinShortSequenceLength),
            Math.Max(Math.Max(1, config.MinShortSequenceLength), config.MaxShortSequenceLength) + 1);

        for (int i = 0; i < sequenceLength; i++)
        {
            int watchMs = _random.Next(
                Math.Max(1000, config.MinShortWatchMilliseconds),
                Math.Max(Math.Max(1000, config.MinShortWatchMilliseconds), config.MaxShortWatchMilliseconds) + 1);

            await WatchCurrentVideoAsync(config, token, TimeSpan.FromMilliseconds(watchMs), isShort: true);

            if (i < sequenceLength - 1)
            {
                if (_random.NextDouble() < 0.65)
                {
                    await _page.Keyboard.PressAsync("ArrowDown");
                }
                else
                {
                    await _page.Keyboard.PressAsync("PageDown");
                }

                await Task.Delay(_random.Next(800, 1600), token);
            }
        }

        _shortsInteractions++;
        return true;
    }

    private async Task<bool> RunRecommendationChainAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null)
        {
            return false;
        }

        int chainLength = _random.Next(
            Math.Max(1, config.MinRecommendationChainLength),
            Math.Max(Math.Max(1, config.MinRecommendationChainLength), config.MaxRecommendationChainLength) + 1);
        chainLength = Math.Min(chainLength, Math.Max(1, config.MaxRecommendationDepth));

        bool started = await RunHomeFeedInteractionAsync(config, token);
        if (!started)
        {
            return false;
        }

        for (int step = 1; step < chainLength; step++)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            if (_random.NextDouble() < config.AutoplayFollowProbability)
            {
                _logger?.LogDebug("Waiting for YouTube autoplay to advance to next video.");
                string currentId = ExtractVideoId(_page.Url);
                await Task.Delay(_random.Next(6000, 9000), token);
                if (!string.Equals(currentId, ExtractVideoId(_page.Url), StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogDebug("Autoplay advanced to new video.");
                }
                else
                {
                    await FollowRecommendationAsync(token);
                }
            }
            else
            {
                await FollowRecommendationAsync(token);
            }

            await WatchCurrentVideoAsync(config, token);
        }

        _recommendationInteractions++;
        return true;
    }

    private async Task FollowRecommendationAsync(CancellationToken token)
    {
        if (_page == null || _mouseHelper == null)
        {
            return;
        }

        var recommendations = _page.Locator("#items ytd-compact-video-renderer a#thumbnail, #secondary ytd-compact-video-renderer #video-title");
        int count = await recommendations.CountAsync();
        if (count == 0)
        {
            _logger?.LogDebug("No recommendation thumbnails available to follow.");
            return;
        }

        int index = _random.Next(0, Math.Min(count, 8));
        var recommendation = recommendations.Nth(index);

        try
        {
            await _mouseHelper.MoveAndClickAsync(recommendation);
            await WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed to follow recommendation at index {Index}.", index);
        }
    }

    private async Task<bool> TryOpenShortsFeedAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null || _mouseHelper == null)
        {
            return false;
        }

        try
        {
            var menuItems = _page.Locator("#items ytd-mini-guide-entry-renderer");
            int count = await menuItems.CountAsync();
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                var entry = menuItems.Nth(i);
                string label = (await entry.InnerTextAsync(new() { Timeout = 1000 }))?.Trim() ?? string.Empty;
                if (label.IndexOf("shorts", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await _mouseHelper.MoveAndClickAsync(entry);
                    await WaitForNavigationAsync(token);
                    return true;
                }
            }
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Mini guide navigation to Shorts failed; falling back to direct navigation.");
        }

        string fallback = config.Domains?.FirstOrDefault(d => d.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
            ?? "https://www.youtube.com/shorts";

        try
        {
            await _page.GotoAsync(fallback, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 20000 });
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogWarning(ex, "Failed to open Shorts experience at {Url}.", fallback);
            return false;
        }
    }

    private async Task WatchCurrentVideoAsync(YouTubeWarmupSettings config, CancellationToken token, TimeSpan? explicitDuration = null, bool isShort = false)
    {
        if (_page == null)
        {
            return;
        }

        int min = isShort ? config.MinShortWatchMilliseconds : config.MinWatchMilliseconds;
        int max = isShort ? config.MaxShortWatchMilliseconds : config.MaxWatchMilliseconds;

        if (max < min)
        {
            max = min;
        }

        TimeSpan duration = explicitDuration ?? TimeSpan.FromMilliseconds(_random.Next(Math.Max(1000, min), Math.Max(1000, max) + 1));

        var controlHelper = new PlayerControlHelper(_page, _mouseHelper, _logger);
        var activity = new VideoPlayerActivity(_page, logger: null, controlHelper)
        {
            MinDelayBetweenActionsMs = Math.Max(1200, config.MinDelayBetweenActionsMs),
            MaxDelayBetweenActionsMs = Math.Max(config.MinDelayBetweenActionsMs + 1000, config.MaxDelayBetweenActionsMs)
        };

        try
        {
            await activity.WatchCurrentVideoAsync(duration, token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Watch routine encountered an issue.");
        }

        TrackVisitedVideo(_page.Url);
    }

    private async Task NavigateToHomeAsync(YouTubeWarmupSettings config, CancellationToken token)
    {
        if (_page == null)
        {
            return;
        }

        if (_page.Url.StartsWith("https://www.youtube.com/", StringComparison.OrdinalIgnoreCase) &&
            !_page.Url.Contains("watch", StringComparison.OrdinalIgnoreCase) &&
            !_page.Url.Contains("results", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string home = config.Domains?.FirstOrDefault(d => d.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) && !d.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
            ?? "https://www.youtube.com";

        await _page.GotoAsync(home, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 20000 });
    }

    private async Task WaitForNavigationAsync(CancellationToken token)
    {
        if (_page == null)
        {
            return;
        }

        try
        {
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 30000 });
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Wait for navigation timed out but continuing warmup.");
        }
    }

    private async Task<string> GetRandomKeywordAsync(string? keywordDirectory, CancellationToken token)
    {
        if (_cachedKeywords == null)
        {
            _cachedKeywords = await LoadKeywordsAsync(keywordDirectory, token);
        }

        if (_cachedKeywords.Count == 0)
        {
            return "latest music";
        }

        return _cachedKeywords[_random.Next(_cachedKeywords.Count)];
    }

    private async Task<List<string>> LoadKeywordsAsync(string? directory, CancellationToken token)
    {
        var results = new List<string>();

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.txt"))
                {
                    using var stream = File.OpenRead(file);
                    using var reader = new StreamReader(stream);
                    while (!reader.EndOfStream)
                    {
                        token.ThrowIfCancellationRequested();
                        string? line = await reader.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            results.Add(line.Trim());
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Failed to load YouTube keywords from {Directory}.", directory);
            }
        }

        if (results.Count == 0)
        {
            results.AddRange(new[]
            {
                "daily tech news",
                "travel vlogs",
                "cooking tutorials",
                "music live performance",
                "gaming highlights",
                "productivity tips"
            });
        }

        return results;
    }

    private void TrackVisitedVideo(string? url)
    {
        string id = ExtractVideoId(url);
        if (!string.IsNullOrEmpty(id))
        {
            _visitedVideoIds.Add(id);
        }
    }

    private string ExtractVideoId(string? url)
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

    private async Task PersistSessionStateAsync(YouTubeWarmupSettings config, string? profileId, CancellationToken token)
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
                VisitedVideoIds = _visitedVideoIds.ToArray()
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

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
