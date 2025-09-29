using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Behaviors;

internal sealed class SearchWarmupBehavior : IYouTubeWarmupBehavior
{
    private readonly ILogger<SearchWarmupBehavior>? _logger;
    private List<string>? _cachedKeywords;

    public SearchWarmupBehavior(ILogger<SearchWarmupBehavior>? logger = null)
    {
        _logger = logger;
    }

    public string Name => "Search";

    public YouTubeWarmupBehaviorKind Kind => YouTubeWarmupBehaviorKind.Search;

    public IEnumerable<string> Aliases { get; } = new[] { "search", "searches" };

    public bool Matches(string identifier)
        => Aliases.Any(alias => alias.Equals(identifier, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> ExecuteAsync(WarmupContext context, YouTubeWarmupSettings config, CancellationToken token)
    {
        var page = context.Page;
        var mouse = context.Mouse;
        var keyboard = context.Keyboard;
        if (page == null || mouse == null || keyboard == null)
        {
            return false;
        }

        string keyword = await GetRandomKeywordAsync(context, token);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        await context.NavigateToHomeAsync(config, token);

        var searchInput = page.Locator("input[name='search_query']");
        if (!await searchInput.IsVisibleAsync(new() { Timeout = 3000 }))
        {
            _logger?.LogDebug("Search input not visible; attempting keyboard shortcut.");
            await page.Keyboard.PressAsync("/");
            await Task.Delay(context.Random.Next(200, 450), token);
        }

        if (!await searchInput.IsVisibleAsync(new() { Timeout = 3000 }))
        {
            _logger?.LogWarning("Unable to locate YouTube search box.");
            return false;
        }

        await mouse.MoveAndClickAsync(searchInput);
        await page.Keyboard.PressAsync("Control+A");
        await Task.Delay(context.Random.Next(100, 250), token);
        await page.Keyboard.PressAsync("Backspace");

        await keyboard.TypeLikeHumanAsync(searchInput, keyword);

        if (context.Random.NextDouble() < 0.35)
        {
            await Task.Delay(context.Random.Next(400, 900), token);
            int arrowPresses = context.Random.Next(1, 3);
            await keyboard.PressNavigationKeyAsync("ArrowDown", arrowPresses);
        }

        if (context.Random.NextDouble() < 0.2)
        {
            var searchButton = page.Locator("button[class='ytSearchboxComponentSearchButton']");
            if (await searchButton.IsVisibleAsync(new() { Timeout = 1500 }))
            {
                await mouse.MoveAndClickAsync(searchButton);
            }
            else
            {
                await page.Keyboard.PressAsync("Enter");
            }
        }
        else
        {
            await page.Keyboard.PressAsync("Enter");
        }

        await context.WaitForNavigationAsync(token);

        if (!await WaitForSearchResultsAsync(page, token))
        {
            _logger?.LogWarning("No visible search results located for keyword '{Keyword}'.", keyword);
            return false;
        }

        var results = page.Locator("ytd-video-renderer a#thumbnail, ytd-video-renderer #video-title-link");
        int count = await page.Locator("ytd-video-renderer").CountAsync();
        int index = context.Random.Next(0, Math.Min(count, 5));
        var target = results.Nth(index);

        try
        {
            await target.ScrollIntoViewIfNeededAsync();
            await mouse.MoveAndClickAsync(target);
            await context.WaitForNavigationAsync(token);
            try
            {
                await page.WaitForURLAsync(
                    url => url.Contains("watch", StringComparison.OrdinalIgnoreCase)
                        || url.Contains("/shorts/", StringComparison.OrdinalIgnoreCase),
                    new() { Timeout = 15000 });
            }
            catch (PlaywrightException ex)
            {
                _logger?.LogDebug(ex, "URL wait after opening search result timed out.");
            }
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed to open search result.");
            return false;
        }

        await context.WatchVideoAsync(config, token);
        context.IncrementSearchInteractions();
        return true;
    }

    private async Task<string> GetRandomKeywordAsync(WarmupContext context, CancellationToken token)
    {
        _cachedKeywords ??= await LoadKeywordsAsync(context.KeywordDirectory, token);
        if (_cachedKeywords.Count == 0)
        {
            return "latest music";
        }

        return _cachedKeywords[context.Random.Next(_cachedKeywords.Count)];
    }

    private async Task<bool> WaitForSearchResultsAsync(IPage page, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var results = page.Locator("ytd-video-renderer");
            await results.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 12000 });
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Waiting for search results timed out.");
            return false;
        }
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
}
