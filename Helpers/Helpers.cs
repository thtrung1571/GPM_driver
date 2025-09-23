using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    public static class PlaywrightHelper
    {
        public static async Task<IBrowser> ConnectWithRetryAsync(IPlaywright playwright, string remoteAddress, int maxRetries = 5)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"[Playwright] Attempt {attempt} to connect over CDP...");
                    var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://{remoteAddress}");
                    Console.WriteLine("[Playwright] Connected successfully!");
                    return browser;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Playwright] Failed: {ex.Message}");
                    if (attempt == maxRetries) throw;

                    int wait = RandomProvider.Next(1500, 4000); // jitter wait
                    Console.WriteLine($"Retrying in {wait} ms...");
                    await Task.Delay(wait);
                }
            }
            throw new Exception("Unable to connect after retries.");
        }
    }
}
