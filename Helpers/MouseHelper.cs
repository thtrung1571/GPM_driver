using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace GPM_driver.Helpers
{
    public class MouseHelper
    {
        private readonly IPage _page;
        private readonly Random _random;

        private double _lastX = 0;
        private double _lastY = 0;

        public int MinStepDelay { get; set; } = 8;
        public int MaxStepDelay { get; set; } = 25;
        public int MinClickDelay { get; set; } = 120;
        public int MaxClickDelay { get; set; } = 500;

        public MouseHelper(IPage page)
        {
            _page = page;
            // Use shared RNG to avoid identical seeds across helpers
            _random = RandomProvider.Shared;
        }

        /// <summary>
        /// Move the mouse to a locator with easing + jitter effect
        /// </summary>
        public async Task MoveToAsync(ILocator locator, int steps = 20, bool addJitter = true)
        {
            var box = await locator.BoundingBoxAsync();
            if (box == null) return;

            // Aim somewhere *inside* the element, not always exact center
            double offsetX = (box.Width * 0.4) * (_random.NextDouble() - 0.5);
            double offsetY = (box.Height * 0.4) * (_random.NextDouble() - 0.5);

            double targetX = box.X + (box.Width / 2) + offsetX;
            double targetY = box.Y + (box.Height / 2) + offsetY;

            await MoveToCoordinatesAsync(targetX, targetY, steps, addJitter);
        }

        /// <summary>
        /// Low-level coordinate move with easing + jitter
        /// </summary>
        private async Task MoveToCoordinatesAsync(double targetX, double targetY, int steps, bool addJitter)
        {
            double currentX = _lastX;
            double currentY = _lastY;

            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;

                // Randomly choose easing style per move
                double ease = _random.Next(3) switch
                {
                    0 => t,                                    // linear
                    1 => t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t, // ease in/out
                    _ => Math.Sin(t * Math.PI / 2)             // smooth ease-out
                };

                double newX = currentX + (targetX - currentX) * ease;
                double newY = currentY + (targetY - currentY) * ease;

                if (addJitter)
                {
                    newX += _random.NextDouble() * 2 - 1;
                    newY += _random.NextDouble() * 2 - 1;
                }

                await _page.Mouse.MoveAsync((float)newX, (float)newY);
                await Task.Delay(_random.Next(MinStepDelay, MaxStepDelay));

                _lastX = newX;
                _lastY = newY;
            }
        }

        /// <summary>
        /// Move to locator and click with human-like delay & micro jitter
        /// </summary>
        public async Task MoveAndClickAsync(ILocator locator, int steps = 20, bool addJitter = true)
        {
            await MoveToAsync(locator, steps, addJitter);

            // Small pause before clicking
            await Task.Delay(_random.Next(MinClickDelay, MaxClickDelay));

            // Micro-adjustments before click (like hand wobble)
            for (int i = 0; i < _random.Next(1, 3); i++)
            {
                double jitterX = _lastX + (_random.NextDouble() - 0.5) * 2;
                double jitterY = _lastY + (_random.NextDouble() - 0.5) * 2;
                await _page.Mouse.MoveAsync((float)jitterX, (float)jitterY);
                await Task.Delay(_random.Next(30, 80));
            }

            // Rare chance of slight mis-click, then recovery
            if (_random.NextDouble() < 0.05) // 5% chance
            {
                await _page.Mouse.ClickAsync((float)_lastX + _random.Next(-15, 15), (float)_lastY + _random.Next(-15, 15));
                await Task.Delay(_random.Next(200, 400));
                await MoveToAsync(locator, steps: 12, addJitter: true);
            }

            await locator.ClickAsync();
        }

        /// <summary>
        /// Move to locator and hover with natural pause
        /// </summary>
        public async Task MoveAndHoverAsync(ILocator locator, int steps = 20, bool addJitter = true)
        {
            await MoveToAsync(locator, steps, addJitter);

            // Human hover dwell
            int dwell = _random.Next(800, 3000); // 0.8s to 3s
            await Task.Delay(dwell);
        }

        /// <summary>
        /// Random wandering movement (like idle human mouse moves)
        /// </summary>
        public async Task WanderAsync(int radius = 50, int steps = 15)
        {
            for (int i = 0; i < steps; i++)
            {
                double angle = _random.NextDouble() * 2 * Math.PI;
                double dx = Math.Cos(angle) * radius * _random.NextDouble();
                double dy = Math.Sin(angle) * radius * _random.NextDouble();

                double newX = _lastX + dx;
                double newY = _lastY + dy;

                await _page.Mouse.MoveAsync((float)newX, (float)newY);
                await Task.Delay(_random.Next(20, 60));

                _lastX = newX;
                _lastY = newY;
            }
        }

        /// <summary>
        /// Move mouse randomly within viewport (general wandering)
        /// </summary>
        public async Task MoveRandomlyAsync(int steps = 25)
        {
            var viewport = _page.ViewportSize;

            int width = viewport?.Width ?? 1280;
            int height = viewport?.Height ?? 720;

            double targetX = _random.Next(0, width);
            double targetY = _random.Next(0, height);

            await MoveToCoordinatesAsync(targetX, targetY, steps, addJitter: true);

            await Task.Delay(_random.Next(200, 1000));
        }

        /// <summary>
        /// Moves the mouse towards an absolute coordinate in the viewport using the same easing logic
        /// as element-based moves. Useful for lightweight jitter or targeting screen regions.
        /// </summary>
        public Task MoveAsync(double x, double y, int steps = 15, bool addJitter = true)
            => MoveToCoordinatesAsync(x, y, steps, addJitter);

        /// <summary>
        /// Convenience overload for integer coordinates.
        /// </summary>
        public Task MoveAsync(int x, int y, int steps = 15, bool addJitter = true)
            => MoveAsync((double)x, (double)y, steps, addJitter);
    }
}
