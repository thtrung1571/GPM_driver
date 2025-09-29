using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Behaviors;

internal sealed class HomeWarmupBehavior : IYouTubeWarmupBehavior
{
    private readonly ILogger<HomeWarmupBehavior>? _logger;

    public HomeWarmupBehavior(ILogger<HomeWarmupBehavior>? logger = null)
    {
        _logger = logger;
    }

    public string Name => "Home";

    public YouTubeWarmupBehaviorKind Kind => YouTubeWarmupBehaviorKind.Home;

    public IEnumerable<string> Aliases { get; } = new[] { "home", "feed" };

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

        await context.NavigateToHomeAsync(config, token);

        var items = page.Locator("ytd-rich-grid-row ytd-rich-item-renderer a#thumbnail, ytd-rich-item-renderer #video-title-link");
        int count = await items.CountAsync();
        if (count == 0)
        {
            _logger?.LogWarning("Home feed did not return any clickable videos.");
            return false;
        }

        int index = context.Random.Next(0, Math.Min(count, 8));
        var item = items.Nth(index);
        try
        {
            await mouse.MoveAndClickAsync(item);
            await context.WaitForNavigationAsync(token);
        }
        catch (PlaywrightException ex)
        {
            _logger?.LogDebug(ex, "Failed clicking home feed item {Index}.", index);
            return false;
        }

        await context.WatchVideoAsync(config, token);
        context.IncrementHomeInteractions();
        return true;
    }
}
