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
    public class IpFighterService
    {
        private readonly IPage _page;

        public IpFighterService(IPage page)
        {
            _page = page;
        }

        public async Task<IpFighterResult> CheckIpAsync()
        {
            await _page.GotoAsync("https://ipfighter.com/vi", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            // Add random delay between 5-10 seconds
            var delayMilliseconds = RandomProvider.Next(5000, 10001);
            await Task.Delay(delayMilliseconds);

            var result = new IpFighterResult
            {
                Isp = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(5) > div > b")),
                Blacklist = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(10) > div > div")),
                Proxy = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(9) > div > div")),
                WebRTC = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(7) > div > div")),
                Score = await TryReadTextContentAsync(_page.Locator(".CircularProgressbar-text")),
                City = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(2) > div > b")),
                Country = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(1) > div > b")),
                Hostname = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(4) > div > b")),
                BlacklistDetails = string.Empty,
                BlacklistServers = string.Empty,
            };
            result.DNS = await TryReadInnerTextAsync(_page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(6) > div > div"));

            // Click “More” then get blacklist details
            if (await TryClickAsync(_page.GetByRole(AriaRole.Button, new() { Name = "More" })))
            {
                await _page.Locator("div.home_detailRateScore__RD62j").WaitForAsync(new() { Timeout = 5000 });
                result.BlacklistDetails = await TryReadInnerTextAsync(
                    _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div.home_itemScore__Jrnyw.align-items-start > div.home_content__iZ3a6")
                );
            }

            // Click “Show” then get blacklist servers
            if (await TryClickAsync(_page.GetByRole(AriaRole.Button, new() { Name = "Show" })))
            {
                await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div:nth-child(2)").WaitForAsync(new() { Timeout = 5000 });
                result.BlacklistServers = await TryReadInnerTextAsync(
                    _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div:nth-child(2)")
                );
            }

            return result;
        }

        private static async Task<string> TryReadInnerTextAsync(ILocator locator, string defaultValue = "")
        {
            try
            {
                await locator.WaitForAsync(new() { Timeout = 5000 });
                return await locator.InnerTextAsync() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static async Task<string> TryReadTextContentAsync(ILocator locator, string defaultValue = "")
        {
            try
            {
                await locator.WaitForAsync(new() { Timeout = 5000 });
                return await locator.TextContentAsync() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static async Task<bool> TryClickAsync(ILocator locator)
        {
            try
            {
                await locator.ClickAsync(new() { Timeout = 5000 });
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IpFighterService] Failed to click {await DescribeAsync(locator)}: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> DescribeAsync(ILocator locator)
        {
            try
            {
                return await locator.EvaluateAsync<string>("el => el.outerHTML") ?? locator.ToString();
            }
            catch
            {
                return locator.ToString();
            }
        }
    }
}