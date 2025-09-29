using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Behaviors;

internal sealed class ShortsWarmupBehavior : IYouTubeWarmupBehavior
{
    private readonly ILogger<ShortsWarmupBehavior>? _logger;

    public ShortsWarmupBehavior(ILogger<ShortsWarmupBehavior>? logger = null)
    {
        _logger = logger;
    }

    public string Name => "Shorts";

    public YouTubeWarmupBehaviorKind Kind => YouTubeWarmupBehaviorKind.Shorts;

    public IEnumerable<string> Aliases { get; } = new[] { "short", "shorts" };

    public bool Matches(string identifier)
        => Aliases.Any(alias => alias.Equals(identifier, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> ExecuteAsync(WarmupContext context, YouTubeWarmupSettings config, CancellationToken token)
    {
        var page = context.Page;
        var mouse = context.Mouse;
        if (page == null || mouse == null)
        {
            return false;
        }

        bool navigated = await TryOpenShortsFeedAsync(context, config, token);
        if (!navigated)
        {
            return false;
        }

        if (!await WaitForShortsPlayerAsync(context, token))
        {
            _logger?.LogWarning("Unable to detect Shorts player after navigation.");
            return false;
        }

        int sequenceLength = context.Random.Next(
            Math.Max(1, config.MinShortSequenceLength),
            Math.Max(Math.Max(1, config.MinShortSequenceLength), config.MaxShortSequenceLength) + 1);

        for (int i = 0; i < sequenceLength; i++)
        {
            int watchMs = context.Random.Next(
                Math.Max(1000, config.MinShortWatchMilliseconds),
                Math.Max(Math.Max(1000, config.MinShortWatchMilliseconds), config.MaxShortWatchMilliseconds) + 1);

            await context.WatchVideoAsync(config, token, TimeSpan.FromMilliseconds(watchMs), isShort: true);

            if (i < sequenceLength - 1)
            {
                string key = context.Random.NextDouble() < 0.65 ? "ArrowDown" : "PageDown";
                await page.Keyboard.PressAsync(key);
                await Task.Delay(context.Random.Next(800, 1600), token);
                if (!await WaitForShortsPlayerAsync(context, token))
                {
                    _logger?.LogDebug("Shorts player did not become ready after advancing.");
                    break;
                }
            }
        }

        context.IncrementShortsInteractions();
        return true;
    }

    private async Task<bool> TryOpenShortsFeedAsync(WarmupContext context, YouTubeWarmupSettings config, CancellationToken token)
    {
        var page = context.Page;
        var mouse = context.Mouse;
        if (page == null || mouse == null)
        {
            return false;
        }

        var navigationSelectors = new[]
        {
            "#items ytd-mini-guide-entry-renderer[aria-label*='Shorts' i]",
            "#items ytd-mini-guide-entry-renderer:has-text('Shorts')",
            "ytd-guide-entry-renderer[aria-label*='Shorts' i] a",
            "a[title*='Shorts' i]",
            "a[aria-label*='Shorts' i]",
            "#endpoint[href^='/shorts']"
        };

        foreach (string selector in navigationSelectors)
        {
            try
            {
                var candidates = page.Locator(selector);
                int count = await candidates.CountAsync();
                for (int i = 0; i < Math.Min(count, 5); i++)
                {
                    var entry = candidates.Nth(i);
                    if (!await entry.IsVisibleAsync(new() { Timeout = 1000 }))
                    {
                        continue;
                    }

                    try
                    {
                        await entry.ScrollIntoViewIfNeededAsync();
                    }
                    catch (PlaywrightException)
                    {
                        // Continue even if scrolling fails; clicking may still succeed.
                    }

                    try
                    {
                        await mouse.MoveAndClickAsync(entry);
                        await context.WaitForNavigationAsync(token);

                        if (page.Url.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (PlaywrightException ex)
                    {
                        _logger?.LogDebug(ex, "Failed to activate Shorts via selector {Selector}.", selector);
                    }
                }
            }
            catch (PlaywrightException ex)
            {
                _logger?.LogTrace(ex, "Selector {Selector} lookup failed while opening Shorts feed.", selector);
            }
        }

        string fallback = config.Domains?.FirstOrDefault(d => d.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
            ?? "https://www.youtube.com/shorts";

        _logger?.LogDebug("Falling back to direct Shorts navigation at {Url}.", fallback);

        try
        {
            await page.GotoAsync(fallback, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 20000 });
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogWarning(ex, "Failed to open Shorts experience at {Url}.", fallback);
            return false;
        }
    }

    private async Task<bool> WaitForShortsPlayerAsync(WarmupContext context, CancellationToken token)
    {
        var page = context.Page;
        if (page == null)
        {
            return false;
        }

        try
        {
            token.ThrowIfCancellationRequested();

            var container = page.Locator("#shorts-player, #reel-video-renderer");
            if (await container.CountAsync() == 0)
            {
                _logger?.LogDebug("Shorts container elements not found after navigation.");
                return false;
            }

            await container.First.WaitForAsync(new()
            {
                State = WaitForSelectorState.Visible,
                Timeout = 12000
            });
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Shorts container did not become visible in time.");
            return false;
        }

        return await context.PlayerControls.WaitForPlayerReadyAsync(token, 15000);
    }
}
