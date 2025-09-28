using System.Threading.Tasks;

using GPM_driver.Helpers;
using GPM_driver.Services.YouTube.Core;
using GPM_driver.Services.YouTube.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class RecommendationActivity : BaseActivity
{
    private readonly VideoPlayerActivity _videoPlayer;

    internal RecommendationActivity(ILogger logger, VideoPlayerActivity videoPlayer)
        : base(YouTubeWarmupBehavior.Recommendations, logger)
    {
        _videoPlayer = videoPlayer;
    }

    protected override async Task<bool> ExecuteAsync(WarmupContext context)
    {
        if (YouTubeUrlHelper.GetVideoKind(context.Page.Url) != YouTubeUrlHelper.YouTubeVideoKind.Video)
        {
            return false;
        }

        var recommendations = context.Page.Locator("#secondary ytd-compact-video-renderer a#thumbnail");
        var available = await recommendations.CountAsync();
        if (available == 0)
        {
            return false;
        }

        var targetIndex = context.Random.Next(available);
        var target = recommendations.Nth(targetIndex);
        string? href = null;
        try
        {
            href = await target.GetAttributeAsync("href");
            await target.ScrollIntoViewIfNeededAsync();
            await target.ClickAsync(new() { Delay = context.Random.Next(30, 100) });
            await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
        }
        catch (PlaywrightException ex)
        {
            Logger.LogDebug(ex, "Failed to open recommendation.");
            return false;
        }

        return await _videoPlayer.WatchCurrentVideoAsync(context, href ?? "recommendation");
    }
}
