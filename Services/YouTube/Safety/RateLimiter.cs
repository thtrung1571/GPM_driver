using System;
using System.Threading;
using System.Threading.Tasks;

namespace GPM_driver.Services.YouTube.Safety;

internal sealed class RateLimiter
{
    private readonly Random _random;
    private DateTime _nextAllowedAction = DateTime.UtcNow;

    internal RateLimiter(Random random)
    {
        _random = random;
    }

    internal async Task WaitForTurnAsync(int minDelayMs, int maxDelayMs, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now < _nextAllowedAction)
        {
            var wait = _nextAllowedAction - now;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }

        var delay = _random.Next(minDelayMs, maxDelayMs + 1);
        _nextAllowedAction = DateTime.UtcNow.AddMilliseconds(delay);
    }
}
