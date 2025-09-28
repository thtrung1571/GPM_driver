using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class LikeDislikeActivity
{
    private readonly ILogger _logger;

    internal LikeDislikeActivity(ILogger logger)
    {
        _logger = logger;
    }

    internal async Task MaybeLikeAsync(WarmupContext context)
    {
        if (context.Random.NextDouble() > 0.35)
        {
            return;
        }

        try
        {
            var likeButton = context.Page.Locator("ytd-toggle-button-renderer#like-button button").First;
            if (!await likeButton.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return;
            }

            await likeButton.HoverAsync();
            await likeButton.ClickAsync(new() { Delay = context.Random.Next(40, 140) });
            context.ActivityLog.Add("Left a like on video");
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to like video during warmup.");
        }
    }

    internal async Task MaybeDislikeAsync(WarmupContext context)
    {
        if (context.Random.NextDouble() > 0.05)
        {
            return;
        }

        try
        {
            var dislikeButton = context.Page.Locator("ytd-toggle-button-renderer#dislike-button button").First;
            if (!await dislikeButton.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return;
            }

            await dislikeButton.ClickAsync(new() { Delay = context.Random.Next(40, 140) });
            context.ActivityLog.Add("Left a dislike on video");
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to dislike video during warmup.");
        }
    }
}
