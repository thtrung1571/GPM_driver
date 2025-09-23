using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Actions
{
    internal class TypingBehavior
    {
        private readonly KeyboardHelper _keyboardHelper;

        public TypingBehavior(KeyboardHelper keyboardHelper)
        {
            _keyboardHelper = keyboardHelper;
        }

        public async Task TypeRandomQueryAsync(ILocator searchBox, string query)
        {
            if (searchBox == null) return;
            await _keyboardHelper.TypeLikeHumanAsync(searchBox, query);
        }

        public async Task TypeInElementAsync(ILocator locator, string text)
        {
            if (locator == null) return;
            await _keyboardHelper.TypeLikeHumanAsync(locator, text);
        }
    }
}