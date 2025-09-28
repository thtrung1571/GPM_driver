using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using GPM_driver.Services.YouTube.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class HomeActivity : BaseActivity
{
    private readonly VideoPlayerActivity _videoPlayer;

    internal HomeActivity(ILogger logger, VideoPlayerActivity videoPlayer)
        : base(YouTubeWarmupBehavior.Home, logger)
    {
        _videoPlayer = videoPlayer;
    }

    protected override async Task<bool> ExecuteAsync(WarmupContext context)
    {
        var url = context.NavigationPattern.GetHomeUrl();
        await context.Page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
        await context.NavigationPattern.RandomScrollAsync(context.Page, context.CancellationToken);
        context.ActivityLog.Add($"Browsed home feed at {context.Page.Url}");

        var videos = context.Page.Locator("ytd-rich-item-renderer a#video-title-link, ytd-rich-grid-media a#video-title-link");
        var count = await videos.CountAsync();
        if (count == 0)
        {
            return true;
        }

        var index = context.Random.Next(Math.Min(count, 6));
        var target = videos.Nth(index);
        string? href = null;

        try
        {
            href = await target.GetAttributeAsync("href");
            await target.ScrollIntoViewIfNeededAsync();
            await target.ClickAsync(new() { Delay = context.Random.Next(40, 120) });
            await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Failed to open home video suggestion.");
            return true;
        }

        return await _videoPlayer.WatchCurrentVideoAsync(context, href ?? "home");
    }
}
