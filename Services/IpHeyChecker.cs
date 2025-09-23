using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Helpers;
using GPM_driver.Models;

namespace GPM_driver.Services
{
    public class IpHeyService
    {
        private readonly IPage _page;

        public IpHeyService(IPage page)
        {
            _page = page;
        }

        public async Task<IpHeyResult> CheckAsync()
        {
            await _page.GotoAsync("https://iphey.com", new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded });
            //await _page.GotoAsync("https://iphey.com", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            //await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await _page.Locator(".identity-status__status:not(.hide) span").WaitForAsync(new() { Timeout = 60000 });
            // Add random delay between 5-10 seconds to mimic reading
            await Task.Delay(RandomProvider.Next(5000, 10001));

            return new IpHeyResult
            {
                Status = await TryInnerTextAsync(_page.Locator(".identity-status__status:not(.hide) span")),
                Browser = await TryInnerTextAsync(_page.Locator(".identity-check__item:nth-of-type(1) .identity-check__text")),
                Location = await TryInnerTextAsync(_page.Locator(".identity-check__item:nth-of-type(2) .identity-check__text")),
                Ip = await TryInnerTextAsync(_page.Locator(".identity-check__item:nth-of-type(3) .identity-check__text")),
                Hardware = await TryInnerTextAsync(_page.Locator(".identity-check__item:nth-of-type(4) .identity-check__text")),
                Software = await TryInnerTextAsync(_page.Locator(".identity-check__item:nth-of-type(5) .identity-check__text"))
            };
        }

        private static async Task<string> TryInnerTextAsync(ILocator locator, string fallback = "")
        {
            try
            {
                await locator.WaitForAsync(new() { Timeout = 5000 });
                return await locator.InnerTextAsync() ?? fallback;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IpHeyService] Failed to read locator: {ex.Message}");
                return fallback;
            }
        }
    }
}