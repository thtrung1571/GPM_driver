using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Helpers;

/// <summary>
/// Provides convenience helpers for issuing navigation key presses with optional logging and
/// realistic timing. The API mirrors what the warmup activities use when interacting with rich
/// media players.
/// </summary>
internal static class NavigationPattern
{
    private static readonly Random Random = RandomProvider.Shared;

    /// <summary>
    /// Attempts to press the provided key using the page keyboard. A probability gate is available
    /// so callers can model infrequent actions without additional plumbing. Returns <c>true</c> when
    /// the key was actually pressed.
    /// </summary>
    public static async Task<bool> TryPressAsync(
        IPage page,
        string key,
        double probability = 1.0,
        int minDelayMs = 50,
        int maxDelayMs = 150,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (probability < 1.0 && Random.NextDouble() > probability)
        {
            return false;
        }

        try
        {
            await page.Keyboard.PressAsync(key);

            if (maxDelayMs > 0)
            {
                int clampedMin = Math.Max(0, minDelayMs);
                int clampedMax = Math.Max(clampedMin, maxDelayMs);
                int delay = Random.Next(clampedMin, clampedMax + 1);
                if (delay > 0)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            logger?.LogTrace("Pressed key '{Key}' on page {Url}.", key, page.Url);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed pressing key '{Key}' on {Url}.", key, page.Url);
            return false;
        }
    }
}
