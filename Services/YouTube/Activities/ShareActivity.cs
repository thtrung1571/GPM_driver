using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class ShareActivity
{
    private readonly ILogger _logger;

    internal ShareActivity(ILogger logger)
    {
        _logger = logger;
    }

    internal async Task MaybeShareAsync(WarmupContext context)
    {
        if (context.Random.NextDouble() > 0.1)
        {
            return;
        }

        try
        {
            var shareButton = context.Page.Locator("ytd-button-renderer#share-button button").First;
            if (!await shareButton.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return;
            }

            await shareButton.ClickAsync(new() { Delay = context.Random.Next(40, 120) });
            await context.TimingPattern.PauseAsync(context.Random.Next(1200, 2200), context.CancellationToken);
            var closeButton = context.Page.Locator("ytd-share-sheet-renderer tp-yt-icon-button[aria-label]").First;
            if (await closeButton.IsVisibleAsync(new() { Timeout = 1000 }))
            {
                await closeButton.ClickAsync();
            }

            context.ActivityLog.Add("Opened share dialog");
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to share during warmup.");
        }
    }
}
