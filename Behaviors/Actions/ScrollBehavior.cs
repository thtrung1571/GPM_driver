using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Actions
{
    internal class ScrollBehavior
    {
        public ScrollBehavior()
        {
            // No instance ScrollHelper because your ScrollHelper is static.
        }

        public async Task SmoothScrollAsync(IPage page)
        {
            // Slow, natural scrolling using the static ScrollHelper
            await ScrollHelper.ScrollRandomAsync(page, scrolls: 5);
        }

        public async Task BurstScrollAsync(IPage page)
        {
            // Quick bursts using keyboard-like scrolling
            await ScrollHelper.ScrollWithKeysAsync(page, keyPresses: 3);
        }

        public async Task ScrollToElementAsync(IPage page, ILocator locator)
        {
            await ScrollHelper.ScrollToElementAsync(page, locator, step: 200);
        }

        public async Task IdleTopBottomScrollAsync(IPage page)
        {
            await ScrollHelper.ScrollToTopAsync(page);
            await Task.Delay(300);
            await ScrollHelper.ScrollToBottomAsync(page);
        }
    }
}
