using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Behaviors;

internal sealed class RecommendationWarmupBehavior : IYouTubeWarmupBehavior
{
    private readonly ILogger<RecommendationWarmupBehavior>? _logger;

    public RecommendationWarmupBehavior(ILogger<RecommendationWarmupBehavior>? logger = null)
    {
        _logger = logger;
    }

    public string Name => "Recommendations";

    public YouTubeWarmupBehaviorKind Kind => YouTubeWarmupBehaviorKind.Recommendations;

    public IEnumerable<string> Aliases { get; } = new[] { "recommendation", "recommendations", "upnext" };

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

        int chainLength = context.Random.Next(
            Math.Max(1, config.MinRecommendationChainLength),
            Math.Max(Math.Max(1, config.MinRecommendationChainLength), config.MaxRecommendationChainLength) + 1);
        chainLength = Math.Min(chainLength, Math.Max(1, config.MaxRecommendationDepth));

        bool started = await StartFromHomeAsync(context, config, token);
        if (!started)
        {
            return false;
        }

        for (int step = 1; step < chainLength; step++)
        {
            token.ThrowIfCancellationRequested();

            if (context.Random.NextDouble() < config.AutoplayFollowProbability)
            {
                string currentId = context.ExtractVideoId(page.Url);
                await Task.Delay(context.Random.Next(6000, 9000), token);
                if (!string.Equals(currentId, context.ExtractVideoId(page.Url), StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogDebug("Autoplay advanced to new video.");
                }
                else
                {
                    await FollowRecommendationAsync(context, token);
                }
            }
            else
            {
                await FollowRecommendationAsync(context, token);
            }

            await context.WatchVideoAsync(config, token);
        }

        context.IncrementRecommendationInteractions();
        return true;
    }

    private async Task<bool> StartFromHomeAsync(WarmupContext context, YouTubeWarmupSettings config, CancellationToken token)
    {
        var page = context.Page;
        var mouse = context.Mouse;
        if (page == null || mouse == null)
        {
            return false;
        }

        await context.NavigateToHomeAsync(config, token);

        if (!await WaitForHomeFeedAsync(page, token))
        {
            _logger?.LogWarning("Home feed did not return any clickable videos for recommendation chain.");
            return false;
        }

        var items = page.Locator("ytd-rich-grid-row ytd-rich-item-renderer a#thumbnail, ytd-rich-item-renderer #video-title-link");
        int count = await items.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("Home feed did not return any clickable videos for recommendation chain.");
            return false;
        }

        int index = context.Random.Next(0, Math.Min(count, 6));
        var item = items.Nth(index);
        try
        {
            await item.ScrollIntoViewIfNeededAsync();
            await mouse.MoveAndClickAsync(item);
            await context.WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed to open initial video for recommendation chain.");
            return false;
        }

        await context.WatchVideoAsync(config, token);
        return true;
    }

    private async Task FollowRecommendationAsync(WarmupContext context, CancellationToken token)
    {
        var page = context.Page;
        var mouse = context.Mouse;
        if (page == null || mouse == null)
        {
            return;
        }

        if (!await WaitForRecommendationPanelAsync(page, token))
        {
            _logger?.LogDebug("No recommendation thumbnails available to follow.");
            return;
        }

        var recommendations = page.Locator("#items ytd-compact-video-renderer a#thumbnail, #secondary ytd-compact-video-renderer #video-title");
        int count = await recommendations.CountAsync();
        if (count == 0)
        {
            _logger?.LogDebug("No recommendation thumbnails available to follow.");
            return;
        }

        int index = context.Random.Next(0, Math.Min(count, 8));
        var recommendation = recommendations.Nth(index);

        try
        {
            await recommendation.ScrollIntoViewIfNeededAsync();
            await mouse.MoveAndClickAsync(recommendation);
            await context.WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed to follow recommendation at index {Index}.", index);
        }
    }

    private async Task<bool> WaitForHomeFeedAsync(IPage page, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var gridItems = page.Locator("ytd-rich-grid-row ytd-rich-item-renderer");
            await gridItems.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 12000 });
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Timed out waiting for YouTube home feed before recommendation chain.");
            return false;
        }
    }

    private async Task<bool> WaitForRecommendationPanelAsync(IPage page, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var panelItems = page.Locator("#items ytd-compact-video-renderer, #secondary ytd-compact-video-renderer");
            await panelItems.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Timed out waiting for recommendation panel items.");
            return false;
        }
    }
}
