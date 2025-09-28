using System;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

internal sealed class CommentActivity
{
    private static readonly string[] Comments =
    {
        "Really enjoyed this segment!",
        "Adding this to my watch later list.",
        "Great insights, thanks for sharing.",
        "This was surprisingly helpful!"
    };

    private readonly ILogger _logger;

    internal CommentActivity(ILogger logger)
    {
        _logger = logger;
    }

    internal async Task MaybeCommentAsync(WarmupContext context)
    {
        if (context.Random.NextDouble() > 0.05)
        {
            return;
        }

        try
        {
            await context.NavigationPattern.RandomScrollAsync(context.Page, context.CancellationToken);
            var commentBox = context.Page.Locator("ytd-comment-simplebox-renderer #placeholder-area").First;
            if (!await commentBox.IsVisibleAsync(new() { Timeout = 3000 }))
            {
                return;
            }

            await commentBox.ClickAsync();
            var input = context.Page.Locator("ytd-comment-dialog-renderer #contenteditable-root, #contenteditable-root[contenteditable]").First;
            if (!await input.IsVisibleAsync(new() { Timeout = 2000 }))
            {
                return;
            }

            var message = Comments[context.Random.Next(Comments.Length)];
            await input.TypeAsync(message, new() { Delay = context.Random.Next(40, 120) });
            await context.TimingPattern.PauseAsync(context.Random.Next(500, 1200), context.CancellationToken);
            await context.Page.Keyboard.PressAsync("Escape");
            context.ActivityLog.Add("Drafted a comment");
        }
        catch (PlaywrightException ex)
        {
            _logger.LogDebug(ex, "Failed to draft comment during warmup.");
        }
    }
}
