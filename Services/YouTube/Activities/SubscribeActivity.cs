using System;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class SubscribeActivity
{
    private readonly ILogger _logger;

    internal SubscribeActivity(ILogger logger)
    {
        _logger = logger;
    }

    internal async Task MaybeSubscribeAsync(WarmupContext context)
    {
        if (context.Random.NextDouble() > 0.25)
        {
            return;
        }

        try
        {
            var subscribeButton = context.Page.Locator("#subscribe-button ytd-subscribe-button-renderer tp-yt-paper-button, #subscribe-button button").First;
            if (!await subscribeButton.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return;
            }

            var label = await subscribeButton.GetAttributeAsync("aria-pressed");
            if (string.Equals(label, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await subscribeButton.ClickAsync(new() { Delay = context.Random.Next(40, 120) });
            context.ActivityLog.Add("Subscribed to channel");
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to subscribe during warmup.");
        }
    }
}
