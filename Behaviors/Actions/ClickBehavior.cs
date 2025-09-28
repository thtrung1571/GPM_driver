using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Actions
{
    internal class ClickBehavior
    {
        private readonly MouseHelper _mouseHelper;
        private readonly Random _random;

        public ClickBehavior(MouseHelper mouseHelper)
        {
            _mouseHelper = mouseHelper ?? throw new ArgumentNullException(nameof(mouseHelper));
            _random = RandomProvider.Shared;
        }

        public async Task SafeRandomLinkClickAsync(IPage page)
        {
            // LinkHelper is static in your codebase — call it directly
            await LinkHelper.SafeClickRandomInternalLinkAsync(page, _mouseHelper);
        }

        public async Task HoverAndClickAsync(ILocator locator)
        {
            if (locator == null) return;

            // hover first
            await _mouseHelper.MoveAndHoverAsync(locator);
            await Task.Delay(_random.Next(100, 400));
            // then click (MoveAndClick will move again, but that's OK for realism)
            await _mouseHelper.MoveAndClickAsync(locator);
        }

        public async Task HoverOnlyAsync(ILocator locator)
        {
            if (locator == null) return;
            await _mouseHelper.MoveAndHoverAsync(locator);
        }
    }
}