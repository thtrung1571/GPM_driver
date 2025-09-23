using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    internal static class ScrollHelper
    {
        private static readonly Random _random = new Random(Guid.NewGuid().GetHashCode());

        public static async Task ScrollRandomAsync(IPage page, int scrolls = 5, bool allowUpward = true)
        {
            try
            {
                int pageHeight = await GetPageHeightAsync(page);
                int viewportHeight = await page.EvaluateAsync<int>("() => window.innerHeight");
                
                if (pageHeight <= viewportHeight)
                {
                    // Page is too short to scroll meaningfully
                    Console.WriteLine("[ScrollHelper] Page too short for meaningful scrolling");
                    return;
                }

                for (int i = 0; i < scrolls; i++)
                {
                    int currentScrollY = await page.EvaluateAsync<int>("() => window.scrollY");
                    int maxScroll = Math.Max(0, pageHeight - viewportHeight);
                    
                    // Determine scroll direction and magnitude
                    int deltaY;
                    if (allowUpward && currentScrollY > 0 && _random.NextDouble() < 0.3)
                    {
                        // 30% chance to scroll up if we're not at top
                        deltaY = -_random.Next(100, Math.Min(400, currentScrollY));
                    }
                    else
                    {
                        // Mostly scroll down
                        deltaY = _random.Next(100, 600);
                    }

                    int target = Math.Max(0, Math.Min(currentScrollY + deltaY, maxScroll));

                    // Use smooth scrolling for more natural behavior
                    await page.EvaluateAsync(@"
                        y => window.scrollTo({
                            top: y,
                            behavior: 'smooth'
                        })", target);
                    
                    // Variable timing between scrolls
                    await Task.Delay(_random.Next(800, 2500));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScrollHelper] ScrollRandomAsync failed: {ex.Message}");
            }
        }

        public static async Task ScrollToBottomAsync(IPage page)
        {
            int pageHeight = await GetPageHeightAsync(page);

            await page.EvaluateAsync(@$"
                (h) => new Promise(resolve => {{
                    let distance = 200;
                    let timer = setInterval(() => {{
                        let scrollTop = window.scrollY;
                        window.scrollBy(0, distance);

                        if (scrollTop + window.innerHeight >= h) {{
                            clearInterval(timer);
                            resolve(true);
                        }}
                    }}, 200);
                }})
            ", pageHeight);
        }

        public static async Task ScrollToTopAsync(IPage page)
        {
            await page.EvaluateAsync(@"
                () => new Promise(resolve => {
                    let distance = 200;
                    let timer = setInterval(() => {
                        let scrollTop = window.scrollY;
                        window.scrollBy(0, -distance);

                        if (scrollTop <= 0) {
                            clearInterval(timer);
                            resolve(true);
                        }
                    }, 200);
                })
            ");
        }

        public static async Task ScrollWithKeysAsync(IPage page, int keyPresses = 5)
        {
            string[] keys = { "ArrowDown", "ArrowUp", "PageDown", "PageUp" };

            for (int i = 0; i < keyPresses; i++)
            {
                string key = keys[_random.Next(keys.Length)];
                await page.Keyboard.PressAsync(key);

                await Task.Delay(_random.Next(400, 1200));
            }
        }

        /// <summary>
        /// Scroll smoothly until element is visible, with easing and clamping.
        /// </summary>
        public static async Task ScrollToElementAsync(IPage page, ILocator locator, int step = 200)
        {
            try
            {
                var box = await locator.BoundingBoxAsync();
                if (box == null)
                {
                    Console.WriteLine("[ScrollHelper] Element has no bounding box, cannot scroll to it");
                    return;
                }

                int viewportHeight = await page.EvaluateAsync<int>("() => window.innerHeight");
                int elementY = (int)box.Y;
                int targetY = Math.Max(0, elementY - (viewportHeight / 3)); // aim so element is not glued to top

                int currentY = await page.EvaluateAsync<int>("() => window.scrollY");

                // If element is already reasonably visible, don't scroll
                if (Math.Abs(currentY - targetY) < viewportHeight / 4)
                {
                    Console.WriteLine("[ScrollHelper] Element already reasonably visible, skipping scroll");
                    return;
                }

                // Smooth scroll with human-like steps
                while (Math.Abs(currentY - targetY) > step)
                {
                    int direction = currentY < targetY ? 1 : -1;
                    int stepSize = Math.Min(step, Math.Abs(currentY - targetY));
                    currentY += direction * stepSize;

                    await page.EvaluateAsync(@"
                        y => window.scrollTo({
                            top: y,
                            behavior: 'smooth'
                        })", currentY);
                    
                    await Task.Delay(_random.Next(150, 400));
                }

                // Final adjustment
                await page.EvaluateAsync(@"
                    y => window.scrollTo({
                        top: y,
                        behavior: 'smooth'
                    })", targetY);
                
                await Task.Delay(_random.Next(300, 700));
                
                // Verify element is now visible
                bool isVisible = await locator.IsVisibleAsync();
                if (!isVisible)
                {
                    Console.WriteLine("[ScrollHelper] Element still not visible after scroll");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScrollHelper] ScrollToElementAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Natural reading-style scrolling with pauses
        /// </summary>
        public static async Task ReadingScrollAsync(IPage page, int sections = 3, int readingPauseMs = 3000)
        {
            try
            {
                int pageHeight = await GetPageHeightAsync(page);
                int viewportHeight = await page.EvaluateAsync<int>("() => window.innerHeight");
                
                if (pageHeight <= viewportHeight) return;

                int scrollPerSection = (pageHeight - viewportHeight) / sections;
                
                for (int i = 0; i < sections; i++)
                {
                    // Scroll down one section
                    int targetY = Math.Min((i + 1) * scrollPerSection, pageHeight - viewportHeight);
                    
                    await page.EvaluateAsync(@"
                        y => window.scrollTo({
                            top: y,
                            behavior: 'smooth'
                        })", targetY);
                    
                    // Reading pause with some variation
                    int pauseTime = readingPauseMs + _random.Next(-1000, 1000);
                    await Task.Delay(Math.Max(1000, pauseTime));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScrollHelper] ReadingScrollAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Quick scan scrolling - faster with shorter pauses
        /// </summary>
        public static async Task ScanScrollAsync(IPage page, int bursts = 5)
        {
            try
            {
                for (int i = 0; i < bursts; i++)
                {
                    int scrollAmount = _random.Next(200, 800);
                    int currentY = await page.EvaluateAsync<int>("() => window.scrollY");
                    
                    await page.EvaluateAsync(@"
                        amount => window.scrollBy({
                            top: amount,
                            behavior: 'smooth'
                        })", scrollAmount);
                    
                    // Short pause between bursts
                    await Task.Delay(_random.Next(300, 1000));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScrollHelper] ScanScrollAsync failed: {ex.Message}");
            }
        }

        private static async Task<int> GetPageHeightAsync(IPage page)
        {
            return await page.EvaluateAsync<int>("() => document.body.scrollHeight");
        }
    }
}
