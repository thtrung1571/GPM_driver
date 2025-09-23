using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    internal static class LinkHelper
    {
        private static readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        /// <summary>
        /// Safely picks a random visible internal link and clicks it.
        /// Handles new tab popups automatically with improved error handling.
        /// </summary>
        /// <param name="page">Current page</param>
        /// <param name="mouseHelper">Optional MouseHelper for human-like movement</param>
        /// <param name="maxAttempts">Maximum attempts to find and click a link</param>
        /// <returns>The page that was navigated to (could be the same page or a new popup page). Null if failed.</returns>
        public static async Task<IPage?> SafeClickRandomInternalLinkAsync(
            IPage page,
            MouseHelper? mouseHelper = null,
            int maxAttempts = 3)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Console.WriteLine($"[LinkHelper] Attempt {attempt}/{maxAttempts} to find clickable links");

                    // More robust link collection with better selectors
                    var linkLocators = await CollectClickableInternalLinksAsync(page);
                    
                    if (linkLocators.Count == 0)
                    {
                        Console.WriteLine("[LinkHelper] No clickable internal links found.");
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(_random.Next(1000, 2000)); // Wait before retry
                            continue;
                        }
                        return null;
                    }

                    // Pick random link locator
                    var chosenLocator = linkLocators[_random.Next(linkLocators.Count)];
                    
                    // Get href for logging
                    string? href = null;
                    try
                    {
                        href = await chosenLocator.GetAttributeAsync("href");
                    }
                    catch
                    {
                        href = "unknown";
                    }

                    Console.WriteLine($"[LinkHelper] Clicking internal link: {href}");

                    // Ensure element is in viewport
                    await ScrollHelper.ScrollToElementAsync(page, chosenLocator);
                    await Task.Delay(_random.Next(200, 500)); // Brief pause after scrolling

                    // Check if element is still visible and enabled
                    if (!await chosenLocator.IsVisibleAsync() || !await chosenLocator.IsEnabledAsync())
                    {
                        Console.WriteLine("[LinkHelper] Selected link is no longer clickable, retrying...");
                        continue;
                    }

                    var context = page.Context;
                    
                    // Setup popup detection with timeout
                    Task<IPage>? popupTask = null;
                    try
                    {
                        popupTask = context.WaitForPageAsync(new() { Timeout = 3000 });
                    }
                    catch
                    {
                        // If popup detection fails, continue without it
                    }

                    // Perform the click
                    if (mouseHelper != null)
                        await mouseHelper.MoveAndClickAsync(chosenLocator);
                    else
                        await chosenLocator.ClickAsync(new() { Timeout = 5000 });

                    // Handle navigation/popup detection
                    if (popupTask != null)
                    {
                        var finished = await Task.WhenAny(popupTask, Task.Delay(2000));
                        if (finished == popupTask && !popupTask.IsFaulted)
                        {
                            try
                            {
                                var newPage = await popupTask;
                                await newPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 10000 });
                                Console.WriteLine("[LinkHelper] Link opened in new tab.");
                                return newPage;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[LinkHelper] New tab handling failed: {ex.Message}");
                            }
                        }
                    }

                    // Check if same page navigated
                    try
                    {
                        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 8000 });
                        Console.WriteLine("[LinkHelper] Navigation completed in same tab.");
                        return page;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LinkHelper] Navigation timeout: {ex.Message}");
                        return page; // Return page anyway, might still be usable
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LinkHelper] Attempt {attempt} failed: {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(_random.Next(1000, 2000)); // Wait before retry
                        continue;
                    }
                }
            }

            Console.WriteLine("[LinkHelper] All attempts failed.");
            return null;
        }

        /// <summary>
        /// Collects all clickable internal links from the page with improved filtering.
        /// </summary>
        private static async Task<List<ILocator>> CollectClickableInternalLinksAsync(IPage page)
        {
            var results = new List<ILocator>();
            
            try
            {
                var baseUri = new Uri(page.Url);
                
                // Get all visible anchor elements with href attributes
                var allLinks = page.Locator("a[href]:visible");
                int count = await allLinks.CountAsync();
                
                for (int i = 0; i < Math.Min(count, 50); i++) // Limit to first 50 links for performance
                {
                    try
                    {
                        var link = allLinks.Nth(i);
                        var href = await link.GetAttributeAsync("href");
                        
                        if (string.IsNullOrWhiteSpace(href))
                            continue;
                            
                        // Enhanced filtering
                        if (!IsClickableInternalLink(href, baseUri))
                            continue;
                            
                        // Check if element is actually clickable
                        if (await link.IsVisibleAsync() && await link.IsEnabledAsync())
                        {
                            results.Add(link);
                        }
                    }
                    catch
                    {
                        // Skip problematic individual links
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LinkHelper] Error collecting links: {ex.Message}");
            }

            Console.WriteLine($"[LinkHelper] Found {results.Count} clickable internal links");
            return results;
        }

        /// <summary>
        /// Enhanced internal link detection with better filtering.
        /// </summary>
        private static bool IsClickableInternalLink(string href, Uri baseUri)
        {
            if (string.IsNullOrWhiteSpace(href))
                return false;

            // Filter out non-clickable protocols
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("sms:", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Skip pure anchors (but allow relative URLs with anchors)
            if (href == "#" || href.StartsWith("#"))
                return false;

            // Relative URLs are internal
            if (href.StartsWith("/"))
                return true;

            // Handle relative URLs without leading slash
            if (!href.Contains("://") && !href.StartsWith("//"))
                return true;

            // Absolute URLs - check if same host
            if (Uri.TryCreate(href, UriKind.Absolute, out var linkUri))
            {
                return string.Equals(linkUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
            }

            // Protocol-relative URLs (//)
            if (href.StartsWith("//"))
            {
                if (Uri.TryCreate($"{baseUri.Scheme}:{href}", UriKind.Absolute, out var protocolRelativeUri))
                {
                    return string.Equals(protocolRelativeUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Additional utility: Find and hover over a random link without clicking.
        /// </summary>
        public static async Task<bool> HoverRandomInternalLinkAsync(
            IPage page,
            MouseHelper? mouseHelper = null)
        {
            try
            {
                var linkLocators = await CollectClickableInternalLinksAsync(page);
                
                if (linkLocators.Count == 0)
                {
                    Console.WriteLine("[LinkHelper] No links found for hovering.");
                    return false;
                }

                var chosenLocator = linkLocators[_random.Next(linkLocators.Count)];
                
                // Scroll to element
                await ScrollHelper.ScrollToElementAsync(page, chosenLocator);
                
                if (mouseHelper != null)
                {
                    await mouseHelper.MoveAndHoverAsync(chosenLocator);
                }
                else
                {
                    await chosenLocator.HoverAsync();
                }

                Console.WriteLine("[LinkHelper] Hovered over random internal link.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LinkHelper] Hover failed: {ex.Message}");
                return false;
            }
        }
    }
}
