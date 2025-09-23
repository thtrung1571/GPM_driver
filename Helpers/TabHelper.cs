using Microsoft.Playwright;
using System;
using System.Threading.Tasks;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Behaviors;

namespace GPM_driver.Helpers
{
    /// <summary>
    /// Helper for managing browser tabs and new page exploration
    /// </summary>
    internal static class TabHelper
    {
        private static readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        /// <summary>
        /// ClickExplorer-specific behavior for exploring new tabs
        /// </summary>
        public static async Task ExploreNewTabAsync(IPage newPage, MouseHelper mouse, int maxDurationMs)
        {
            try
            {
                var start = DateTime.UtcNow;
                var end = start.AddMilliseconds(maxDurationMs);
                
                BehaviorLogger.LogAction("NewTabExploration", $"duration={maxDurationMs}ms");

                // Wait for initial load
                try
                {
                    await newPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 5000 });
                }
                catch
                {
                    BehaviorLogger.LogAction("NewTabExploration", "load-timeout");
                }

                while (DateTime.UtcNow < end)
                {
                    double action = _random.NextDouble();
                    
                    if (action < 0.4)
                    {
                        // Quick content scan
                        await BehaviorBase.PerformReadingBehaviorAsync(newPage, mouse, intensity: 1);
                    }
                    else if (action < 0.7)
                    {
                        // Look for more links to explore
                        bool foundLink = await LinkHelper.HoverRandomInternalLinkAsync(newPage, mouse);
                        if (!foundLink)
                        {
                            // Fallback: general content inspection
                            await BehaviorBase.PerformContentInspectionAsync(newPage, mouse);
                        }
                    }
                    else
                    {
                        // Brief scrolling exploration
                        await ScrollHelper.ScrollRandomAsync(newPage, _random.Next(1, 3));
                    }

                    // Short pause between actions in new tab
                    await Task.Delay(_random.Next(300, 1000));
                    
                    // Check if we should continue
                    if (DateTime.UtcNow >= end) break;
                }
            }
            catch (Exception ex)
            {
                BehaviorLogger.LogAction("NewTabExplorationError", ex.Message);
            }
        }

        /// <summary>
        /// Safely close a tab with error handling
        /// </summary>
        public static async Task<bool> SafeCloseTabAsync(IPage page)
        {
            try
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TabHelper] Failed to close tab: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Bring a page to front safely
        /// </summary>
        public static async Task<bool> SafeBringToFrontAsync(IPage page)
        {
            try
            {
                if (!page.IsClosed)
                {
                    await page.BringToFrontAsync();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TabHelper] Failed to bring tab to front: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get basic page information for logging
        /// </summary>
        public static async Task<string> GetPageInfoAsync(IPage page)
        {
            try
            {
                if (page.IsClosed) return "closed-tab";
                
                string title = "unknown";
                string url = "unknown";
                
                try { title = await page.TitleAsync(); } catch { }
                try { url = page.Url; } catch { }
                
                return $"{title} ({url})";
            }
            catch
            {
                return "error-getting-info";
            }
        }
    }
}