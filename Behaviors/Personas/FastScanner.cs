using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Personas
{
    internal class FastScanner : IPersona
    {
        private readonly Random _rng = new Random(Guid.NewGuid().GetHashCode());

        public async Task PerformAsync(IPage page, MouseHelper mouse, KeyboardHelper keyboard, RetentionBucket bucket, int durationSeconds)
        {
            BehaviorLogger.Log($"FastScanner starting for {durationSeconds}s (bucket={bucket})");

            var end = DateTime.UtcNow.AddSeconds(durationSeconds <= 0 ? 5 : durationSeconds);
            if (durationSeconds <= 0) await Task.Delay(500);

            // Check if page has sufficient content
            bool hasContent = await BehaviorBase.HasSufficientContentAsync(page);
            
            while (DateTime.UtcNow < end)
            {
                try
                {
                    double choice = _rng.NextDouble();

                    if (choice < 0.65)
                    {
                        // Quick scroll patterns - vary by content availability
                        int scrolls = hasContent ? _rng.Next(1, 4) : _rng.Next(1, 2);
                        await ScrollHelper.ScrollRandomAsync(page, scrolls);
                        BehaviorLogger.LogAction("ScrollRandom", $"bursts={scrolls}");
                    }
                    else if (choice < 0.85)
                    {
                        // Fast keyboard navigation
                        int presses = _rng.Next(1, 3);
                        await ScrollHelper.ScrollWithKeysAsync(page, presses);
                        BehaviorLogger.LogAction("ScrollWithKeys", $"presses={presses}");
                    }
                    else if (choice < 0.95)
                    {
                        // Brief hover interactions (scanning behavior)
                        if (hasContent && _rng.NextDouble() < 0.6)
                        {
                            bool hovered = await LinkHelper.HoverRandomInternalLinkAsync(page, mouse);
                            BehaviorLogger.LogAction("LinkHover", hovered ? "success" : "failed");
                            if (hovered)
                            {
                                // Quick peek time
                                await Task.Delay(_rng.Next(300, 800));
                            }
                        }
                        else
                        {
                            await mouse.MoveRandomlyAsync(_rng.Next(8, 20));
                            BehaviorLogger.LogAction("MouseMove", "random-scan");
                        }
                    }
                    else
                    {
                        // Occasionally click links (but close quickly - scanning behavior)
                        var clickedPage = await LinkHelper.SafeClickRandomInternalLinkAsync(page, mouse, maxAttempts: 2);
                        if (clickedPage == null)
                        {
                            BehaviorLogger.LogAction("LinkClick", "none-found-or-failed");
                            // Fallback to content inspection
                            await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                        }
                        else if (!object.ReferenceEquals(clickedPage, page))
                        {
                            // New tab - quick peek and close (fast scanner behavior)
                            BehaviorLogger.LogAction("LinkClick", "opened-new-tab");
                            await Task.Delay(_rng.Next(800, 2500)); // Shorter peek time for fast scanner
                            try
                            {
                                await clickedPage.CloseAsync();
                            }
                            catch { /* ignore */ }
                            try { await page.BringToFrontAsync(); } catch { }
                        }
                        else
                        {
                            BehaviorLogger.LogAction("LinkClick", "navigated-same-tab");
                        }
                    }

                    // Fast scanner has more frequent micro-movements
                    if (_rng.NextDouble() < 0.7)
                    {
                        await mouse.MoveRandomlyAsync(_rng.Next(5, 15));
                    }

                    // Shorter reading pauses - characteristic of fast scanning
                    int pauseMs = TimingDistributions.NormalMs(_rng, meanMs: 500, stdDevMs: 300, minMs: 150, maxMs: 2500);
                    await Task.Delay(pauseMs);
                }
                catch (Exception ex)
                {
                    BehaviorLogger.LogAction("Error", ex.Message);
                    await Task.Delay(300); // Shorter error recovery for fast scanner
                }
            }

            BehaviorLogger.Log("FastScanner finished");
        }
    }
}
