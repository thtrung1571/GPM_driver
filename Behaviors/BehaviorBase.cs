using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors
{
    /// <summary>
    /// Base class for shared behavior utilities across personas.
    /// Contains common helper methods that all personas can use.
    /// </summary>
    internal static class BehaviorBase
    {
        private static readonly Random _random = RandomProvider.Shared;

        /// <summary>
        /// Performs a random content inspection behavior (text selection, hover over elements, etc.)
        /// </summary>
        public static async Task PerformContentInspectionAsync(IPage page, MouseHelper mouse)
        {
            try
            {
                double choice = _random.NextDouble();

                if (choice < 0.4)
                {
                    // Hover over random text elements
                    await HoverOverTextElementsAsync(page, mouse);
                }
                else if (choice < 0.7)
                {
                    // Simulate text selection behavior
                    await SimulateTextSelectionAsync(page, mouse);
                }
                else
                {
                    // Random element hovering
                    await mouse.MoveRandomlyAsync(_random.Next(10, 25));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BehaviorBase] Content inspection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Hover over random text elements like paragraphs, headings, etc.
        /// </summary>
        private static async Task HoverOverTextElementsAsync(IPage page, MouseHelper mouse)
        {
            try
            {
                var textElements = page.Locator("p, h1, h2, h3, h4, h5, h6, span, div");
                int count = await textElements.CountAsync();
                
                if (count > 0)
                {
                    int targetIndex = _random.Next(Math.Min(count, 20)); // Limit to first 20 elements
                    var target = textElements.Nth(targetIndex);
                    
                    if (await target.IsVisibleAsync())
                    {
                        await ScrollHelper.ScrollToElementAsync(page, target);
                        await mouse.MoveAndHoverAsync(target);
                        await Task.Delay(_random.Next(500, 2000));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BehaviorBase] Text element hover failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates text selection behavior by clicking and dragging
        /// </summary>
        private static async Task SimulateTextSelectionAsync(IPage page, MouseHelper mouse)
        {
            try
            {
                var paragraphs = page.Locator("p:visible");
                int count = await paragraphs.CountAsync();
                
                if (count > 0)
                {
                    var target = paragraphs.Nth(_random.Next(Math.Min(count, 10)));
                    
                    if (await target.IsVisibleAsync())
                    {
                        await ScrollHelper.ScrollToElementAsync(page, target);
                        
                        // Simulate text selection with triple-click (selects paragraph)
                        if (_random.NextDouble() < 0.3) // 30% chance
                        {
                            await mouse.MoveAndClickAsync(target);
                            await Task.Delay(_random.Next(100, 300));
                            await page.Mouse.ClickAsync(0, 0, new() { ClickCount = 3 });
                            await Task.Delay(_random.Next(1000, 3000));
                            
                            // Deselect by clicking elsewhere
                            await mouse.MoveRandomlyAsync(_random.Next(5, 15));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BehaviorBase] Text selection simulation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a random page interaction that simulates reading behavior
        /// </summary>
        public static async Task PerformReadingBehaviorAsync(IPage page, MouseHelper mouse, int intensity = 1)
        {
            try
            {
                double choice = _random.NextDouble();
                
                if (choice < 0.5)
                {
                    // Scroll with reading-like patterns
                    await ScrollHelper.ScrollRandomAsync(page, _random.Next(1, intensity + 1));
                }
                else if (choice < 0.8)
                {
                    // Use keyboard for navigation
                    await ScrollHelper.ScrollWithKeysAsync(page, _random.Next(1, Math.Max(1, intensity)));
                }
                else
                {
                    // Hover over content as if reading
                    await PerformContentInspectionAsync(page, mouse);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BehaviorBase] Reading behavior failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if current page has sufficient content for behavior simulation
        /// </summary>
        public static async Task<bool> HasSufficientContentAsync(IPage page)
        {
            try
            {
                // Check for basic content indicators
                var textElements = page.Locator("p, article, main, .content, #content");
                var linkElements = page.Locator("a[href]");
                
                int textCount = await textElements.CountAsync();
                int linkCount = await linkElements.CountAsync();
                
                return textCount > 3 || linkCount > 5;
            }
            catch
            {
                return true; // Assume content exists if check fails
            }
        }
    }
}