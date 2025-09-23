using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<GoogleSearchService>? _logger;

        public GoogleSearchService(IBrowserContext context, ILogger<GoogleSearchService>? logger = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger;
        }

        private async Task<IPage> EnsurePageAsync()
        {
            if (_page != null && !_page.IsClosed)
            {
                return _page;
            }

            var existing = _context.Pages?.FirstOrDefault(p => !p.IsClosed);
            if (existing != null)
            {
                _page = existing;
            }
            else
            {
                _logger?.LogDebug("No reusable Google page found. Creating a new page instance.");
                _page = await _context.NewPageAsync();
            }

            _mouseHelper = new MouseHelper(_page);
            _keyboardHelper = new KeyboardHelper(_page);

            return _page;
        }

        public async Task RunWarmupAsync(
            string keywordFolder,
            GoogleWarmupSettings warmupSettings,
            Func<IPage?, Task>? onResultVisited = null)
        {
            if (warmupSettings is null)
            {
                throw new ArgumentNullException(nameof(warmupSettings));
            }

            int completed = 0;
            while (true)
            {
                string keyword = await PickRandomKeywordAsync(keywordFolder);
                await SearchKeywordAsync(keyword);
                var visitedPage = await PersonaClickRandomResultAsync();

                if (visitedPage != null && onResultVisited != null)
                {
                    await onResultVisited(visitedPage);
                }

                completed++;
                bool underMax = completed < warmupSettings.MaxSearches;
                if (!underMax)
                {
                    _logger?.LogInformation("Reached configured maximum of {Completed} Google warmup searches.", completed);
                    break;
                }

                if (completed < warmupSettings.MinSearches)
                {
                    _logger?.LogInformation(
                        "Completed {Completed} Google searches but minimum is {Minimum}. Continuing warmup.",
                        completed,
                        warmupSettings.MinSearches);
                    await PrepareForNextSearchAsync(warmupSettings);
                    continue;
                }

                double roll = _random.NextDouble();
                if (roll >= warmupSettings.ContinueProbability)
                {
                    _logger?.LogInformation(
                        "Ending Google warmup after {Completed} searches (roll={Roll:0.00} threshold={Threshold:0.00}).",
                        completed,
                        roll,
                        warmupSettings.ContinueProbability);
                    break;
                }

                _logger?.LogInformation(
                    "Continuing Google warmup after {Completed} searches (roll={Roll:0.00}).",
                    completed,
                    roll);
                await PrepareForNextSearchAsync(warmupSettings);
            }
        }

        public async Task SearchKeywordAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                throw new ArgumentException("keyword must not be empty", nameof(keyword));
            }

            var page = await EnsurePageAsync();
            _logger?.LogInformation("Starting Google search for '{Keyword}'.", keyword);

            var curUrl = (page.Url ?? string.Empty).ToLowerInvariant();
            bool alreadyOnGoogle = IsOnGoogle(curUrl);

            if (!alreadyOnGoogle)
            {
                string target = ChooseDomain();
                _logger?.LogInformation("Navigating from '{CurrentUrl}' to '{TargetUrl}' for Google warmup.", curUrl, target);
                await page.GotoAsync(target, new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded });
            }
            else
            {
                _logger?.LogDebug("Reusing existing Google page: {Url}.", curUrl);
            }

            var input = await EnsureSearchInputAsync();
            await _mouseHelper.MoveAndClickAsync(input);
            await ClearSearchBoxAsync(input);
            await _keyboard_helper_TypeLikeHumanAsync(input, keyword);

            await Task.Delay(_random.Next(500, 2001));

            double choice = _random.NextDouble();

            if (choice < 0.25)
            {
                var suggestions = page.Locator("//*[@id='Alh6id']/div[1]/div/ul/li");
                int count = await suggestions.CountAsync();
                if (count > 0)
                {
                    int index = _random.Next(1, count + 1);
                    var suggestion = page.Locator($"//*[@id='Alh6id']/div[1]/div/ul/li[{index}]");
                    await Task.Delay(_random.Next(500, 2001));
                    await _mouseHelper.MoveAndClickAsync(suggestion);
                    _logger?.LogInformation("Clicked Google suggestion #{Index}.", index);
                    return;
                }

                await input.PressAsync("Enter");
                _logger?.LogInformation("No suggestions available; pressed Enter.");
                return;
            }

            if (choice < 0.5)
            {
                var suggestions = page.Locator("//*[@id='Alh6id']/div[1]/div/ul/li");
                int count = await suggestions.CountAsync();
                int times = count > 0 ? _random.Next(1, count + 1) : _random.Next(1, 4);
                for (int i = 0; i < times; i++)
                {
                    await input.PressAsync("ArrowDown");
                    await Task.Delay(_random.Next(50, 501));
                }
                await input.PressAsync("Enter");
                _logger?.LogInformation("Pressed ArrowDown {Times} times then Enter.", times);
                return;
            }

            if (choice < 0.75)
            {
                var searchBtn = page.Locator("//div[@class='lJ9FBc']//input[@name='btnK']");
                if (await searchBtn.CountAsync() > 0 && await searchBtn.IsVisibleAsync())
                {
                    await Task.Delay(_random.Next(500, 2001));
                    await _mouseHelper.MoveAndClickAsync(searchBtn);
                    _logger?.LogInformation("Clicked Google search button (btnK).");
                    return;
                }

                await input.PressAsync("Enter");
                _logger?.LogInformation("Search button unavailable; pressed Enter.");
                return;
            }

            await input.PressAsync("Enter");
            _logger?.LogInformation("Pressed Enter to execute Google search.");
        }

        public async Task<IPage?> PersonaClickRandomResultAsync()
        {
            var page = await EnsurePageAsync();

            var resultsLocator = page.Locator("//div[@class='yuRUbf']");
            await resultsLocator.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

            int maxSites = await resultsLocator.CountAsync();
            if (maxSites == 0)
            {
                _logger?.LogWarning("No Google search results found to click.");
                return null;
            }

            double decision = _random.NextDouble();
            if (decision < 0.3)
            {
                int bursts = _random.Next(1, 4);
                await ScrollHelper.ScrollRandomAsync(page, bursts);
                _logger?.LogInformation("Persona scrolled randomly ({Bursts} bursts) before clicking.", bursts);
            }
            else if (decision < 0.5)
            {
                await ScrollHelper.ScrollToBottomAsync(page);
                _logger?.LogInformation("Persona scrolled to the bottom before selecting a result.");
            }
            else
            {
                _logger?.LogDebug("Persona chose not to scroll before clicking.");
            }

            int targetIndex = _random.Next(1, maxSites + 1);
            var targetSite = page.Locator($"(//div[@class='yuRUbf']//a[@jsname='UWckNb'])[{targetIndex}]");
            _logger?.LogInformation("Clicking Google result #{Index} of {Total}.", targetIndex, maxSites);

            await ScrollHelper.ScrollToElementAsync(page, targetSite);

            try
            {
                await _mouseHelper.MoveAndClickAsync(targetSite);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "First click on result #{Index} failed, retrying.", targetIndex);
                await Task.Delay(_random.Next(500, 1500));
                await _mouseHelper.MoveAndClickAsync(targetSite);
            }

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            _logger?.LogInformation("Navigation to selected result completed.");
            return page;
        }

        public async Task<IPage> SearchOneRandomKeywordAsync(string keywordFolder)
        {
            string keyword = await PickRandomKeywordAsync(keywordFolder);
            await SearchKeywordAsync(keyword);
            var currentPage = await PersonaClickRandomResultAsync();
            return currentPage ?? _page;
        }

        public async Task<string> PickRandomKeywordAsync(string keywordFolder)
        {
            if (!Directory.Exists(keywordFolder))
            {
                throw new DirectoryNotFoundException($"Keyword folder not found: {keywordFolder}");
            }

            string[] files = Directory.GetFiles(keywordFolder, "*.txt");
            if (files.Length == 0)
            {
                throw new FileNotFoundException("No keyword files in folder.", keywordFolder);
            }

            string file = files[_random.Next(files.Length)];
            var lines = (await File.ReadAllLinesAsync(file)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0)
            {
                throw new InvalidOperationException($"Keyword file '{file}' is empty.");
            }

            string keyword = lines[_random.Next(lines.Length)].Trim();
            _logger?.LogDebug("Selected keyword '{Keyword}' from '{FileName}'.", keyword, Path.GetFileName(file));
            return keyword;
        }

        public async Task PrepareForNextSearchAsync(GoogleWarmupSettings warmupSettings)
        {
            var page = await EnsurePageAsync();

            await Task.Delay(_random.Next(1500, 4000));
            double strategy = _random.NextDouble();

            if (strategy < 0.35)
            {
                string domain = ChooseDomain(warmupSettings);
                _logger?.LogInformation("Warmup navigating directly to {Domain} for next keyword.", domain);
                await page.GotoAsync(domain, new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded });
                await Task.Delay(_random.Next(500, 1500));
                var input = await EnsureSearchInputAsync();
                await _mouseHelper.MoveAndClickAsync(input);
                await ClearSearchBoxAsync(input);
                return;
            }

            bool returned = await TryReturnToGoogleAsync(warmupSettings);
            if (!returned)
            {
                string fallbackDomain = ChooseDomain(warmupSettings);
                _logger?.LogInformation("Browser history lacked Google page. Navigating to {Domain}.", fallbackDomain);
                await page.GotoAsync(fallbackDomain, new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded });
            }

            bool useSlashShortcut = strategy < 0.7;
            var focusLocator = await FocusSearchInputAsync(useSlashShortcut);
            await ClearSearchBoxAsync(focusLocator);
        }

        private async Task<bool> TryReturnToGoogleAsync(GoogleWarmupSettings warmupSettings)
        {
            var page = await EnsurePageAsync();
            int maxAttempts = Math.Max(1, warmupSettings.MaxBacktracks);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (IsOnGoogle(page.Url))
                {
                    return true;
                }

                var response = await page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
                await Task.Delay(_random.Next(500, 1500));

                if (response == null && !IsOnGoogle(page.Url))
                {
                    break;
                }
            }

            return IsOnGoogle(page.Url);
        }

        private async Task<ILocator> FocusSearchInputAsync(bool useSlashShortcut)
        {
            var page = await EnsurePageAsync();

            if (useSlashShortcut)
            {
                _logger?.LogDebug("Focusing Google search via '/' shortcut.");
                await page.Keyboard.PressAsync("/");
                await Task.Delay(_random.Next(200, 600));
            }

            var input = await EnsureSearchInputAsync();

            bool isFocused = false;
            try
            {
                isFocused = await input.IsFocusedAsync();
            }
            catch
            {
                isFocused = false;
            }

            if (!isFocused)
            {
                _logger?.LogDebug("Search input not focused; clicking element.");
                await _mouseHelper.MoveAndClickAsync(input);
            }

            return input;
        }

        private async Task<ILocator> EnsureSearchInputAsync()
        {
            var page = await EnsurePageAsync();
            var locator = page.Locator("//textarea[@id='APjFqb']");
            if (await locator.CountAsync() == 0)
            {
                locator = page.Locator("input[name='q']");
            }

            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            return locator;
        }

        private async Task ClearSearchBoxAsync(ILocator input)
        {
            string currentValue = string.Empty;
            try
            {
                currentValue = await input.InputValueAsync() ?? string.Empty;
            }
            catch
            {
                currentValue = string.Empty;
            }

            if (string.IsNullOrEmpty(currentValue))
            {
                return;
            }

            bool useSelectAll = _random.NextDouble() < 0.65;
            if (useSelectAll)
            {
                _logger?.LogDebug("Clearing existing query using Ctrl+A.");
                await _page.Keyboard.PressAsync("Control+A");
                await Task.Delay(_random.Next(100, 300));
                string key = _random.NextDouble() < 0.5 ? "Backspace" : "Delete";
                await _page.Keyboard.PressAsync(key);
            }
            else
            {
                _logger?.LogDebug("Clearing existing query using backspace strokes.");
                await _keyboardHelper.BackspaceAsync(currentValue.Length, fastCorrection: true);
            }

            await Task.Delay(_random.Next(200, 500));
        }

        private static bool IsOnGoogle(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            return url.Contains("google.com", StringComparison.OrdinalIgnoreCase);
        }

        private string ChooseDomain(GoogleWarmupSettings? warmupSettings = null)
        {
            string[] domains = warmupSettings?.Domains?.Length > 0
                ? warmupSettings.Domains
                : new[] { "https://www.google.com", "https://www.google.com.vn" };

            return domains[_random.Next(domains.Length)];
        }

        private string ChooseDomain()
        {
            return ChooseDomain(null);
        }

        private async Task _keyboard_helper_TypeLikeHumanAsync(ILocator locator, string text)
        {
            if (_keyboardHelper == null)
            {
                _keyboardHelper = new KeyboardHelper(_page);
            }

            await _keyboardHelper.TypeLikeHumanAsync(locator, text);
        }
    }
}
