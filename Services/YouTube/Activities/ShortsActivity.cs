using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using GPM_driver.Services.YouTube.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class ShortsActivity : BaseActivity
{
    internal ShortsActivity(ILogger logger)
        : base(YouTubeWarmupBehavior.Shorts, logger)
    {
    }

    protected override async Task<bool> ExecuteAsync(WarmupContext context)
    {
        var baseUrl = context.NavigationPattern.GetHomeUrl();
        if (!baseUrl.EndsWith("/"))
        {
            baseUrl += "/";
        }

        await context.Page.GotoAsync(baseUrl + "shorts", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await context.NavigationPattern.EnsureVisibleAsync(context.Page, context.CancellationToken);

        int sequenceLength = context.TimingPattern.NextBetween(
            context.Configuration.Settings.MinShortSequenceLength,
            context.Configuration.Settings.MaxShortSequenceLength);

        for (int i = 0; i < sequenceLength; i++)
        {
            await context.DetectionAvoidance.RandomMouseJitterAsync(context.Mouse, context.CancellationToken);
            var watchTime = context.TimingPattern.NextBetween(
                context.Configuration.Settings.MinShortWatchMilliseconds,
                context.Configuration.Settings.MaxShortWatchMilliseconds);
            await context.TimingPattern.PauseAsync(watchTime, context.CancellationToken);
            context.ActivityLog.Add($"Watched short #{i + 1} for {watchTime}ms");

            if (i + 1 < sequenceLength)
            {
                await context.NavigationPattern.TryPressAsync(context.Page, "ArrowDown", context.CancellationToken);
            }
        }

        return true;
    }
}
