using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
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
            var random = new Random();
            var delayMilliseconds = random.Next(10001, 16000); // 5000ms to 10000ms (5-10 seconds)
            await Task.Delay(delayMilliseconds);

            var result = new IpFighterResult
            {
                Isp = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(5) > div > b").InnerTextAsync(),
                Blacklist = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(10) > div > div").InnerTextAsync(),
                Proxy = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(9) > div > div").InnerTextAsync(),
                WebRTC = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(7) > div > div").InnerTextAsync(),
                Score = await _page.Locator(".CircularProgressbar-text").TextContentAsync() ?? "",
                City = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(2) > div > b").InnerTextAsync(),
                Country = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(1) > div > b").InnerTextAsync(),
                Hostname = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(4) > div > b").InnerTextAsync(),
                DNS = await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_ipInfo__7AV_Z > div:nth-child(6) > div > div").InnerTextAsync()
            };

            // Click “More” then get blacklist details
            await _page.GetByRole(AriaRole.Button, new() { Name = "More" }).ClickAsync();
            await _page.Locator("div.home_detailRateScore__RD62j").WaitForAsync();
            result.BlacklistDetails = await _page.Locator(
                "#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div.home_itemScore__Jrnyw.align-items-start > div.home_content__iZ3a6"
            ).InnerTextAsync();

            // Click “Show” then get blacklist servers
            await _page.GetByRole(AriaRole.Button, new() { Name = "Show" }).ClickAsync();
            await _page.Locator("#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div:nth-child(2)").WaitForAsync();
            result.BlacklistServers = await _page.Locator(
                "#__next > div.home_boxBig__DT6_C > div.containerr > div.home_detailRateScore__RD62j > div > div:nth-child(2)"
            ).InnerTextAsync();

            return result;
        }
    }
}