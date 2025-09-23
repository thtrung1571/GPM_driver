using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    internal class KeyboardHelper
    {
        private readonly IPage _page;
        private readonly Random _random;

        public KeyboardHelper(IPage page)
        {
            _page = page;
            // Use stronger entropy for more realistic behavior
            _random = new Random(Guid.NewGuid().GetHashCode());
        }

        /// <summary>
        /// Types text into an element with random human-like delays and realistic typing patterns.
        /// </summary>
        /// <param name="locator">Locator of the input/textarea element</param>
        /// <param name="text">Text to type</param>
        /// <param name="minDelay">Minimum delay per keystroke (ms)</param>
        /// <param name="maxDelay">Maximum delay per keystroke (ms)</param>
        /// <param name="pauseChance">Chance (0-1) to add a pause after a word</param>
        /// <param name="typoChance">Chance (0-1) to make a typo and correct it</param>
        public async Task TypeLikeHumanAsync(ILocator locator, string text, int minDelay = 50, int maxDelay = 350, double pauseChance = 0.2, double typoChance = 0.03)
        {
            await EnsureElementFocused(locator);
            
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                
                // Simulate occasional typing errors
                if (typoChance > 0 && _random.NextDouble() < typoChance && char.IsLetter(ch))
                {
                    await SimulateTypoAsync(locator, ch);
                }
                else
                {
                    // Normal character typing with variable delays
                    int delay = CalculateRealisticTypingDelay(minDelay, maxDelay, ch, i > 0 ? text[i-1] : '\0');
                    await _page.Keyboard.TypeAsync(ch.ToString(), new() { Delay = delay });
                }

                // Add natural pauses after words or sentences
                if (char.IsWhiteSpace(ch) && _random.NextDouble() < pauseChance)
                {
                    int pause = _random.Next(500, 2001); // 0.5–2s thinking pause
                    await Task.Delay(pause);
                }
                else if (ch == '.' || ch == '!' || ch == '?')
                {
                    // Slight pause after sentences
                    await Task.Delay(_random.Next(200, 800));
                }
            }
        }

        /// <summary>
        /// Types text directly into page keyboard (useful when element already focused).
        /// </summary>
        public async Task TypeLikeHumanAsync(string text, int minDelay = 50, int maxDelay = 350, double pauseChance = 0.2, double typoChance = 0.03)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                
                // Simulate occasional typing errors
                if (typoChance > 0 && _random.NextDouble() < typoChance && char.IsLetter(ch))
                {
                    await SimulateTypoDirectAsync(ch);
                }
                else
                {
                    int delay = CalculateRealisticTypingDelay(minDelay, maxDelay, ch, i > 0 ? text[i-1] : '\0');
                    await _page.Keyboard.TypeAsync(ch.ToString(), new() { Delay = delay });
                }

                if (char.IsWhiteSpace(ch) && _random.NextDouble() < pauseChance)
                {
                    int pause = _random.Next(500, 2001);
                    await Task.Delay(pause);
                }
                else if (ch == '.' || ch == '!' || ch == '?')
                {
                    await Task.Delay(_random.Next(200, 800));
                }
            }
        }

        /// <summary>
        /// Simulates navigation keys like Tab, Enter, Arrow keys with human-like timing
        /// </summary>
        public async Task PressNavigationKeyAsync(string key, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                await _page.Keyboard.PressAsync(key);
                if (i < count - 1)
                {
                    await Task.Delay(_random.Next(200, 600));
                }
            }
        }

        /// <summary>
        /// Simulates realistic backspacing with human-like rhythm
        /// </summary>
        public async Task BackspaceAsync(int count = 1, bool fastCorrection = false)
        {
            int baseDelay = fastCorrection ? 80 : 150;
            int variance = fastCorrection ? 40 : 100;
            
            for (int i = 0; i < count; i++)
            {
                await _page.Keyboard.PressAsync("Backspace");
                if (i < count - 1)
                {
                    await Task.Delay(_random.Next(baseDelay, baseDelay + variance));
                }
            }
        }

        /// <summary>
        /// Ensures an element is focused before typing
        /// </summary>
        private async Task EnsureElementFocused(ILocator locator)
        {
            try
            {
                // Simply focus the element (Playwright will handle if already focused)
                await locator.FocusAsync();
                await Task.Delay(_random.Next(100, 300));
            }
            catch
            {
                // If focus fails, try clicking the element
                try
                {
                    await locator.ClickAsync();
                    await Task.Delay(_random.Next(100, 300));
                }
                catch
                {
                    // If both fail, continue anyway - the typing might still work
                    Console.WriteLine("[KeyboardHelper] Failed to focus element, continuing anyway");
                }
            }
        }

        /// <summary>
        /// Simulates a typo followed by correction
        /// </summary>
        private async Task SimulateTypoAsync(ILocator locator, char intendedChar)
        {
            // Common typo patterns (neighboring keys, etc.)
            char typoChar = GenerateRealisticTypo(intendedChar);
            
            // Type the wrong character
            await _page.Keyboard.TypeAsync(typoChar.ToString(), new() { Delay = _random.Next(50, 200) });
            
            // Pause as if noticing the error
            await Task.Delay(_random.Next(200, 800));
            
            // Backspace and correct
            await BackspaceAsync(1, fastCorrection: true);
            await Task.Delay(_random.Next(100, 400));
            
            // Type the correct character
            await _page.Keyboard.TypeAsync(intendedChar.ToString(), new() { Delay = _random.Next(80, 250) });
        }

        /// <summary>
        /// Direct keyboard typo simulation
        /// </summary>
        private async Task SimulateTypoDirectAsync(char intendedChar)
        {
            char typoChar = GenerateRealisticTypo(intendedChar);
            
            await _page.Keyboard.TypeAsync(typoChar.ToString(), new() { Delay = _random.Next(50, 200) });
            await Task.Delay(_random.Next(200, 800));
            await _page.Keyboard.PressAsync("Backspace");
            await Task.Delay(_random.Next(100, 400));
            await _page.Keyboard.TypeAsync(intendedChar.ToString(), new() { Delay = _random.Next(80, 250) });
        }

        /// <summary>
        /// Generates realistic typing delays based on character combinations
        /// </summary>
        private int CalculateRealisticTypingDelay(int minDelay, int maxDelay, char current, char previous)
        {
            int baseDelay = _random.Next(minDelay, maxDelay + 1);
            
            // Adjust delay based on character combinations
            if (previous != '\0')
            {
                // Same hand typing is typically faster
                if (IsSameHandTyping(previous, current))
                {
                    baseDelay = (int)(baseDelay * 0.8);
                }
                
                // Awkward combinations are slower
                if (IsAwkwardCombination(previous, current))
                {
                    baseDelay = (int)(baseDelay * 1.3);
                }
            }
            
            // Capital letters are slightly slower due to shift
            if (char.IsUpper(current))
            {
                baseDelay = (int)(baseDelay * 1.1);
            }
            
            return Math.Max(minDelay, Math.Min(maxDelay * 2, baseDelay));
        }

        /// <summary>
        /// Generates a realistic typo based on keyboard layout
        /// </summary>
        private char GenerateRealisticTypo(char intended)
        {
            // Simple QWERTY-based neighboring key typos
            var neighbors = intended.ToString().ToLower()[0] switch
            {
                'q' => "wa",
                'w' => "qase",
                'e' => "wsdr",
                'r' => "edft",
                't' => "rfgy",
                'y' => "tghu",
                'u' => "yhji",
                'i' => "ujko",
                'o' => "iklp",
                'p' => "ol",
                'a' => "qwszx",
                's' => "waedxz",
                'd' => "serfcx",
                'f' => "drtgvc",
                'g' => "ftyhbv",
                'h' => "gyujnb",
                'j' => "huikmn",
                'k' => "jiolm",
                'l' => "kop",
                'z' => "asx",
                'x' => "zsdc",
                'c' => "xdfv",
                'v' => "cfgb",
                'b' => "vghn",
                'n' => "bhjm",
                'm' => "njk",
                _ => "abcdefghijklmnopqrstuvwxyz"
            };
            
            if (neighbors.Length > 0)
            {
                char typo = neighbors[_random.Next(neighbors.Length)];
                return char.IsUpper(intended) ? char.ToUpper(typo) : typo;
            }
            
            return intended;
        }

        /// <summary>
        /// Determines if two characters are typically typed with the same hand
        /// </summary>
        private bool IsSameHandTyping(char char1, char char2)
        {
            string leftHand = "qwertasdfgzxcv";
            
            bool char1Left = leftHand.Contains(char.ToLower(char1));
            bool char2Left = leftHand.Contains(char.ToLower(char2));
            
            return char1Left == char2Left;
        }

        /// <summary>
        /// Identifies awkward character combinations that are slower to type
        /// </summary>
        private bool IsAwkwardCombination(char char1, char char2)
        {
            // Common awkward combinations
            string combo = $"{char1}{char2}".ToLower();
            string[] awkward = { "qp", "pq", "zx", "xz", "aq", "qa" };
            
            return Array.Exists(awkward, awkwardCombo => awkwardCombo == combo);
        }
    }
}
