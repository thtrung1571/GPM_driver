using System;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class VideoPlayerActivity
{
    private readonly ILogger _logger;
    private readonly LikeDislikeActivity _likeDislike;
    private readonly SubscribeActivity _subscribe;
    private readonly ShareActivity _share;
    private readonly CommentActivity _comment;

    internal VideoPlayerActivity(
        ILogger logger,
        LikeDislikeActivity likeDislike,
        SubscribeActivity subscribe,
        ShareActivity share,
        CommentActivity comment)
    {
        _logger = logger;
        _likeDislike = likeDislike;
        _subscribe = subscribe;
        _share = share;
        _comment = comment;
    }

    internal async Task<bool> WatchCurrentVideoAsync(WarmupContext context, string origin)
    {
        try
        {
            await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
            var video = context.Page.Locator("video.html5-main-video").First;
            await video.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 45000 });

            // Ensure playback
            await context.Page.Keyboard.PressAsync("k");
            await context.DetectionAvoidance.SmallPauseAsync(context.CancellationToken);

            var totalWatch = context.TimingPattern.NextBetween(
                context.Configuration.Settings.MinWatchMilliseconds,
                context.Configuration.Settings.MaxWatchMilliseconds);

            int segments = context.Random.Next(2, 4);
            for (int i = 0; i < segments; i++)
            {
                await context.DetectionAvoidance.RandomMouseJitterAsync(context.Mouse, context.CancellationToken);
                var segmentDuration = totalWatch / segments;
                segmentDuration = Math.Clamp(segmentDuration, 2000, context.Configuration.Settings.MaxWatchMilliseconds);
                await context.TimingPattern.PauseAsync(segmentDuration, context.CancellationToken);
                if (i == 0)
                {
                    await _likeDislike.MaybeLikeAsync(context);
                }
            }

            await _likeDislike.MaybeDislikeAsync(context);
            await _subscribe.MaybeSubscribeAsync(context);
            await _share.MaybeShareAsync(context);
            await _comment.MaybeCommentAsync(context);

            context.ActivityLog.Add($"Watched video from {origin} for {totalWatch}ms");

            if (context.Random.NextDouble() < context.Configuration.Settings.AutoplayFollowProbability)
            {
                var nextButton = context.Page.Locator("button[aria-label*='Next'], a.ytp-next-button").First;
                if (await nextButton.IsVisibleAsync(new() { Timeout = 2000 }))
                {
                    await nextButton.ClickAsync(new() { Delay = context.Random.Next(30, 110) });
                    await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);
                    context.ActivityLog.Add("Followed autoplay to next video");
                }
            }

            return true;
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to watch video during warmup.");
            return false;
        }
    }
}
