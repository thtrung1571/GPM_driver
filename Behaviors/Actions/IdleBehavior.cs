using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Actions
{
    internal class IdleBehavior
    {
        private readonly MouseHelper _mouseHelper;
        private readonly Random _random;

        public IdleBehavior(MouseHelper mouseHelper)
        {
            _mouseHelper = mouseHelper ?? throw new ArgumentNullException(nameof(mouseHelper));
            _random = new Random(Guid.NewGuid().GetHashCode());
        }

        public async Task RandomPauseAsync(int minMs = 500, int maxMs = 3000)
        {
            int delay = _random.Next(minMs, maxMs + 1);
            // micro jitter while pausing
            if (_random.NextDouble() < 0.4)
            {
                await _mouseHelper.MoveRandomlyAsync(6);
            }
            await Task.Delay(delay);
        }

        public async Task MicroMovementsAsync()
        {
            int steps = _random.Next(3, 12);
            await _mouseHelper.MoveRandomlyAsync(steps);
        }
    }
}
