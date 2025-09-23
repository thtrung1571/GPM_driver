using GPM_driver.Helpers;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GPM_driver.Services
{
    internal class GoogleSearchService
    {
        private readonly IBrowserContext _context;
        private IPage _page;
        private MouseHelper _mouseHelper;
        private KeyboardHelper _keyboardHelper;
        private readonly Random _random = RandomProvider.Shared;

        public GoogleSearchService(IBrowserContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Ensure we have a usable page. Prefer reuse of an existing page in the context (default tab).
        /// Only create a new page if none exist or page was closed.
        /// </summary>
        private async Task<IPage> EnsurePageAsync()
        {
            // If we already have a valid page, return it
            if (_page != null && !_page.IsClosed)
                return _page;

            // Try to reuse any existing open page in the context (prefer the first one)
            var existing = _context.Pages?.FirstOrDefault(p => !p.IsClosed);
            if (existing != null)
            {
                _page = existing;
            }
            else
            {
                // Fallback: create a new page only if there is no page to reuse
                _page = await _context.NewPageAsync();
            }

            // Ensure helpers are bound to the selected page
            _mouseHelper = new MouseHelper(_page);
            _keyboardHelper = new KeyboardHelper(_page);

            return _page;
        }

        /// <summary>
        /// Search a keyword on Google. If current page is already on google.com/.com.vn it will be reused;
        /// otherwise the method navigates the reused page to Google (chooses .com or .com.vn randomly).
        /// </summary>
        public async Task SearchKeywordAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("keyword must not be empty", nameof(keyword));

            var page = await EnsurePageAsync();
            Console.WriteLine($"[GoogleSearch] Starting search for: {keyword}");

            // If current page is not Google (or is blank), navigate to Google (random .com / .com.vn)
            var curUrl = (page.Url ?? string.Empty).ToLowerInvariant();
            bool alreadyOnGoogle = curUrl.Contains("google.com") || curUrl.Contains("google.com.vn");

            if (!alreadyOnGoogle)
            {
                string[] domains = { "https://www.google.com", "https://www.google.com.vn" };
                string url = domains[_random.Next(domains.Length)];
                Console.WriteLine($"[GoogleSearch] Current url is '{curUrl}'. Navigating to {url}");
                await page.GotoAsync(url, new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded });
            }
            else
            {
                Console.WriteLine($"[GoogleSearch] Reusing existing Google page: {curUrl}");
            }

            // Wait for the search input to appear
            var input = page.Locator("//textarea[@id='APjFqb']");
            await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

            // Move to input and focus/click
            await _mouseHelper.MoveAndClickAsync(input);

            // Type keyword like human
            await _keyboard_helper_TypeLikeHumanAsync(input, keyword);

            // small natural pause
            await Task.Delay(_random.Next(500, 2001));

            // Choose search action (suggestion / arrowdown / click btnK / Enter)
            double choice = _random.NextDouble();

            if (choice < 0.25)
            {
                // Click suggestion if present
                var suggestions = page.Locator("//*[@id='Alh6id']/div[1]/div/ul/li");
                int count = await suggestions.CountAsync();
                if (count > 0)
                {
                    int index = _random.Next(1, count + 1);
                    var suggestion = page.Locator($"//*[@id='Alh6id']/div[1]/div/ul/li[{index}]");
                    await Task.Delay(_random.Next(500, 2001));
                    await _mouseHelper.MoveAndClickAsync(suggestion);
                    Console.WriteLine($"[GoogleSearch] Clicked suggestion #{index}");
                    return;
                }
                else
                {
                    await input.PressAsync("Enter");
                    Console.WriteLine("[GoogleSearch] No suggestions, pressed Enter.");
                    return;
                }
            }
            else if (choice < 0.5)
            {
                // ArrowDown selecting based on suggestion count
                var suggestions = page.Locator("//*[@id='Alh6id']/div[1]/div/ul/li");
                int count = await suggestions.CountAsync();
                if (count > 0)
                {
                    int times = _random.Next(1, count + 1);
                    for (int i = 0; i < times; i++)
                    {
                        await input.PressAsync("ArrowDown");
                        await Task.Delay(_random.Next(50, 501));
                    }
                    await input.PressAsync("Enter");
                    Console.WriteLine($"[GoogleSearch] ArrowDown {times} times, then Enter.");
                    return;
                }
                else
                {
                    // fallback behavior
                    int fallback = _random.Next(1, 4);
                    for (int i = 0; i < fallback; i++)
                    {
                        await input.PressAsync("ArrowDown");
                        await Task.Delay(_random.Next(50, 501));
                    }
                    await input.PressAsync("Enter");
                    Console.WriteLine($"[GoogleSearch] No suggestions; ArrowDown x{fallback} then Enter.");
                    return;
                }
            }
            else if (choice < 0.75)
            {
                // Click btnK if visible
                var searchBtn = page.Locator("//div[@class='lJ9FBc']//input[@name='btnK']");
                if (await searchBtn.CountAsync() > 0 && await searchBtn.IsVisibleAsync())
                {
                    await Task.Delay(_random.Next(500, 2001));
                    await _mouseHelper.MoveAndClickAsync(searchBtn);
                    Console.WriteLine("[GoogleSearch] Clicked search button (btnK).");
                    return;
                }
                else
                {
                    await input.PressAsync("Enter");
                    Console.WriteLine("[GoogleSearch] btnK not visible; pressed Enter.");
                    return;
                }
            }
            else
            {
                // Press Enter directly
                await input.PressAsync("Enter");
                Console.WriteLine("[GoogleSearch] Pressed Enter.");
                return;
            }
        }
        /// <summary>
        /// Persona-driven: optionally scroll, then click a random search result.
        /// </summary>
        public async Task<IPage?> PersonaClickRandomResultAsync()
        {
            var page = await EnsurePageAsync();

            // Wait for results
            var resultsLocator = page.Locator("//div[@class='yuRUbf']");
            await resultsLocator.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

            int maxSites = await resultsLocator.CountAsync();
            if (maxSites == 0)
            {
                Console.WriteLine("[GoogleSearch] No search results found.");
                return null;
            }

            // Persona decision: scroll before clicking?
            double decision = _random.NextDouble();
            if (decision < 0.3)
            {
                // 30% chance: scroll randomly
                int bursts = _random.Next(1, 4);
                await ScrollHelper.ScrollRandomAsync(page, bursts);
                Console.WriteLine($"[GoogleSearch] Persona scrolled randomly ({bursts} bursts).");
            }
            else if (decision < 0.5)
            {
                // 20% chance: scroll to bottom
                await ScrollHelper.ScrollToBottomAsync(page);
                Console.WriteLine("[GoogleSearch] Persona scrolled to bottom.");
            }
            else
            {
                // 50% chance: no scroll
                Console.WriteLine("[GoogleSearch] Persona did not scroll before clicking.");
            }

            // Pick target link
            int targetIndex = _random.Next(1, maxSites + 1);
            var targetSite = page.Locator($"(//div[@class='yuRUbf']//a[@jsname='UWckNb'])[{targetIndex}]");
            Console.WriteLine($"[GoogleSearch] Clicking result #{targetIndex} of {maxSites}");

            // Scroll into viewport if needed
            await ScrollHelper.ScrollToElementAsync(page, targetSite);

            // Try click (with retry on misfire)
            try
            {
                await _mouseHelper.MoveAndClickAsync(targetSite);
            }
            catch
            {
                Console.WriteLine("[GoogleSearch] First click failed, retrying...");
                await Task.Delay(_random.Next(500, 1500));
                await _mouseHelper.MoveAndClickAsync(targetSite);
            }

            // Wait for navigation
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            Console.WriteLine("[GoogleSearch] Navigation complete.");
            return page;
        }   
        /// <summary>
        /// Convenience wrapper: pick one random keyword from keywordFolder and run SearchKeywordAsync.
        /// Returns the current page after navigation (may be different from original).
        /// </summary>
        public async Task<IPage> SearchOneRandomKeywordAsync(string keywordFolder)
        {
            if (!Directory.Exists(keywordFolder))
                throw new DirectoryNotFoundException($"Keyword folder not found: {keywordFolder}");

            string[] files = Directory.GetFiles(keywordFolder, "*.txt");
            if (files.Length == 0)
                throw new FileNotFoundException("No keyword files in folder.");

            // pick random file and random non-empty line
            string file = files[_random.Next(files.Length)];
            var lines = (await File.ReadAllLinesAsync(file)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0) throw new InvalidOperationException("Keyword file empty.");

            string keyword = lines[_random.Next(lines.Length)].Trim();
            await SearchKeywordAsync(keyword);
            var currentPage = await PersonaClickRandomResultAsync();
            
            // Return the current page (which might be the navigated page)
            return currentPage ?? _page;
        }

        // Small local wrapper to use KeyboardHelper (keeps class independent if helper changes)
        private async Task _keyboard_helper_TypeLikeHumanAsync(ILocator locator, string text)
        {
            // Ensure keyboard helper exists (EnsurePageAsync has already initialized it)
            if (_keyboardHelper == null)
                _keyboardHelper = new KeyboardHelper(_page);

            await _keyboardHelper.TypeLikeHumanAsync(locator, text);
        }
    }
}