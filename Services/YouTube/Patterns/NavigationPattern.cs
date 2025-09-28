using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Patterns;

internal sealed class NavigationPattern
{
    private readonly Random _random;
    private readonly string[] _domains;

    internal NavigationPattern(Random random, string[] domains)
    {
        _random = random;
        _domains = domains.Length == 0
            ? new[] { "https://www.youtube.com" }
            : domains;
    }

    internal string GetHomeUrl()
    {
        var domain = _domains[_random.Next(_domains.Length)];
        return domain.EndsWith("/", StringComparison.Ordinal) ? domain : domain + "/";
    }

    internal async Task EnsureVisibleAsync(IPage page, CancellationToken cancellationToken)
    {
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 60000 });
        await Task.Delay(_random.Next(500, 1500), cancellationToken);
    }

    internal async Task RandomScrollAsync(IPage page, CancellationToken cancellationToken)
    {
        var scrollSteps = _random.Next(2, 6);
        for (int i = 0; i < scrollSteps; i++)
        {
            int scrollBy = _random.Next(500, 1200);
            await page.EvaluateAsync(
                "window.scrollBy(0, arguments[0]);",
                scrollBy);
            await Task.Delay(_random.Next(500, 2000), cancellationToken);
        }
    }

    internal async Task<bool> TryClickAsync(IPage page, string selector, CancellationToken cancellationToken)
    {
        try
        {
            var locator = page.Locator(selector).First;
            if (await locator.IsVisibleAsync(new() { Timeout = 2000 }) && await locator.IsEnabledAsync())
            {
                await locator.ClickAsync(new() { Delay = _random.Next(50, 150) });
                await Task.Delay(_random.Next(300, 900), cancellationToken);
                return true;
            }
        }
        catch (PlaywrightException)
        {
        }

        return false;
    }

    internal async Task<bool> TryPressAsync(IPage page, string key, CancellationToken cancellationToken)
    {
        try
        {
            await page.Keyboard.PressAsync(key);
            await Task.Delay(_random.Next(200, 600), cancellationToken);
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }
}
