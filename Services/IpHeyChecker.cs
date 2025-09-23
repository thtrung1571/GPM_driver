using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System.Threading.Tasks;
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
            await _page.Locator(".identity-status__status:not(.hide) span").WaitForAsync();
            // Add random delay between 5-10 seconds
            var random = new Random();
            var delayMilliseconds = random.Next(20000, 30000); // 5000ms to 10000ms (5-10 seconds)
            await Task.Delay(delayMilliseconds);

            var result = new IpHeyResult
            {
                Status = await _page.Locator(".identity-status__status:not(.hide) span").InnerTextAsync(),
                Browser = await _page.Locator(".identity-check__item:nth-of-type(1) .identity-check__text").InnerTextAsync(),
                Location = await _page.Locator(".identity-check__item:nth-of-type(2) .identity-check__text").InnerTextAsync(),
                Ip = await _page.Locator(".identity-check__item:nth-of-type(3) .identity-check__text").InnerTextAsync(),
                Hardware = await _page.Locator(".identity-check__item:nth-of-type(4) .identity-check__text").InnerTextAsync(),
                Software = await _page.Locator(".identity-check__item:nth-of-type(5) .identity-check__text").InnerTextAsync()
            };

            return result;
        }
    }
}