using System;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using GPM_driver.Services.YouTube.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class SearchActivity : BaseActivity
{
    private readonly VideoPlayerActivity _videoPlayer;

    internal SearchActivity(ILogger logger, VideoPlayerActivity videoPlayer)
        : base(YouTubeWarmupBehavior.Search, logger)
    {
        _videoPlayer = videoPlayer;
    }

    protected override async Task<bool> ExecuteAsync(WarmupContext context)
    {
        var keyword = context.Configuration.GetRandomKeyword(context.Random);
        var baseUrl = context.NavigationPattern.GetHomeUrl();
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        var searchUrl = baseUrl + "results?search_query=" + Uri.EscapeDataString(keyword);
        await context.Page.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);

        var results = context.Page.Locator("ytd-video-renderer a#video-title");
        var count = await results.CountAsync();
        if (count == 0)
        {
            return false;
        }

        var targetIndex = context.Random.Next(Math.Min(count, 5));
        var target = results.Nth(targetIndex);

        string? href = null;
        try
        {
            href = await target.GetAttributeAsync("href");
            await target.ScrollIntoViewIfNeededAsync();
            await target.ClickAsync(new() { Delay = context.Random.Next(50, 150) });
            await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Failed to open search result for keyword {Keyword}.", keyword);
            return false;
        }

        context.ActivityLog.Add($"Searched for '{keyword}'");
        return await _videoPlayer.WatchCurrentVideoAsync(context, href ?? keyword);
    }
}
