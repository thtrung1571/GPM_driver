using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    public static class PlaywrightHelper
    {
        public static async Task<IBrowser> ConnectWithRetryAsync(
            IPlaywright playwright,
            string remoteAddress,
            int maxRetries = 5,
            ILogger? logger = null)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    logger?.LogInformation("Attempt {Attempt} to connect to Playwright CDP at {RemoteAddress}.", attempt, remoteAddress);
                    var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://{remoteAddress}");
                    logger?.LogInformation("Playwright connected successfully to {RemoteAddress}.", remoteAddress);
                    return browser;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Playwright connection attempt {Attempt} failed.", attempt);
                    if (attempt == maxRetries)
                    {
                        throw;
                    }

                    int wait = RandomProvider.Next(1500, 4000);
                    logger?.LogInformation("Retrying Playwright connection in {Delay} ms.", wait);
                    await Task.Delay(wait);
                }
            }

            throw new Exception("Unable to connect after retries.");
        }
    }
}
