using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System.IO;
using GPM_driver.Helpers;

namespace GPM_driver.Services
{
    public class SmartSearchService
    {
        private readonly IPage _page;
        private readonly IBrowser _browser;
        private readonly string _keywordFolder;
        private readonly Random _random = new Random();

        public SmartSearchService(IPage page, IBrowser browser, string keywordFolder = @"E:\Google_Farm\google\")
        {
            _page = page;
            _browser = browser;
            _keywordFolder = keywordFolder;
        }

        public async Task SearchOneRandomKeywordAsync()
        {
            // 1. Get all keyword files
            var files = Directory.GetFiles(_keywordFolder, "*.txt", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0) throw new Exception("No keyword files found in folder: " + _keywordFolder);

            // 2. Pick a random file
            var file = files[_random.Next(files.Count)];

            // 3. Read keywords and pick a random one
            var lines = File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) throw new Exception("No keywords in file: " + file);

            var keyword = lines[_random.Next(lines.Count)].Trim();
            Console.WriteLine($"[SmartSearch] Using keyword: {keyword}");

            // 4. Pick Bing or Coccoc randomly
            bool useBing = _random.NextDouble() < 0.5;

            if (useBing)
                await SearchBingAsync(keyword);
            else
                await SearchCoccocAsync(keyword);

            Console.WriteLine("[SmartSearch] Search completed!");
        }

        private async Task SearchCoccocAsync(string keyword)
        {
            var context = _page.Context;

            await _page.GotoAsync("https://coccoc.com/search",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

            var input = _page.Locator("//*[@id='main-search-input']");
            await input.WaitForAsync();

            var mouseHelper = new MouseHelper(_page);
            await mouseHelper.MoveAndClickAsync(input);

            foreach (var ch in keyword)
                await input.TypeAsync(ch.ToString(), new() { Delay = _random.Next(50, 351) });

            await Task.Delay(_random.Next(500, 3001));
            await input.PressAsync("Enter");

            // Click Google link (may open in new tab)
            var googleLink = _page.Locator("//a[@href='https://www.google.com/']").First;
            await googleLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await Task.Delay(_random.Next(500, 3001));

            var beforePages = context.Pages.ToList();
            await mouseHelper.MoveAndClickAsync(googleLink);
            await Task.Delay(2000);

            var afterPages = context.Pages.ToList();
            var newPage = afterPages.Except(beforePages).FirstOrDefault();

            if (newPage != null)
            {
                Console.WriteLine("[SmartSearch] Coccoc: Google opened in new tab → closing Coccoc tab.");
                await _page.CloseAsync();
                await newPage.BringToFrontAsync();
            }
            else
            {
                Console.WriteLine("[SmartSearch] Coccoc: Google opened in same tab.");
            }
        }

        private async Task SearchBingAsync(string keyword)
        {
            var context = _page.Context;

            await _page.GotoAsync("https://www.bing.com/",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });

            var input = _page.Locator("//textarea[@id='sb_form_q']");
            await input.WaitForAsync();

            var mouseHelper = new MouseHelper(_page);
            await mouseHelper.MoveAndClickAsync(input);

            foreach (var ch in keyword)
                await input.TypeAsync(ch.ToString(), new() { Delay = _random.Next(50, 351) });

            await Task.Delay(_random.Next(500, 3001));

            // Randomly choose Enter / search button / suggestion
            double choice = _random.NextDouble();
            if (choice < 0.4)
            {
                await input.PressAsync("Enter");
            }
            else if (choice < 0.7)
            {
                var searchBtn = _page.Locator("svg[aria-hidden='true'][width='25']");
                await mouseHelper.MoveAndClickAsync(searchBtn);
            }
            else
            {
                var suggestions = await _page.Locator("ul[role='listbox'] > li[role='option']").CountAsync();
                if (suggestions > 0)
                {
                    int index = _random.Next(1, suggestions + 1);
                    var suggestion = _page.Locator($"ul[role='listbox'] > li[role='option']:nth-child({index})");
                    await mouseHelper.MoveAndClickAsync(suggestion);
                }
                else
                {
                    await input.PressAsync("Enter");
                }
            }

            // Click Google link (may open in new tab)
            var googleLink = _page.Locator("//a[normalize-space()='Google']").First;
            await googleLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await Task.Delay(_random.Next(500, 3001));

            var beforePages = context.Pages.ToList();
            await mouseHelper.MoveAndClickAsync(googleLink);
            await Task.Delay(2000);

            var afterPages = context.Pages.ToList();
            var newPage = afterPages.Except(beforePages).FirstOrDefault();

            if (newPage != null)
            {
                Console.WriteLine("[SmartSearch] Bing: Google opened in new tab → closing Bing tab.");
                await _page.CloseAsync();
                await newPage.BringToFrontAsync();
            }
            else
            {
                Console.WriteLine("[SmartSearch] Bing: Google opened in same tab.");
            }
        }
    }
}