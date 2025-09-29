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

        try
        {
            var menuItems = page.Locator("#items ytd-mini-guide-entry-renderer");
            int count = await menuItems.CountAsync();
            for (int i = 0; i < Math.Min(count, 8); i++)
            {
                var entry = menuItems.Nth(i);
                string label = (await entry.InnerTextAsync(new() { Timeout = 1000 }))?.Trim() ?? string.Empty;
                if (label.IndexOf("shorts", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await mouse.MoveAndClickAsync(entry);
                    await context.WaitForNavigationAsync(token);
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
            var player = page.Locator("#shorts-player video, #reel-video-renderer video, video.html5-main-video");
            await player.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 12000 });
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Shorts player did not become visible in time.");
            return false;
        }

        return await context.PlayerControls.WaitForPlayerReadyAsync(token, 12000);
    }
}
