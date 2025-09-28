using System;
using System.Threading;
using System.Threading.Tasks;

namespace GPM_driver.Services.YouTube.Patterns;

internal sealed class TimingPattern
{
    private readonly Random _random;

    internal TimingPattern(Random random)
    {
        _random = random;
    }

    internal Task DelayBetweenActionsAsync(int minMilliseconds, int maxMilliseconds, CancellationToken cancellationToken)
    {
        var nextDelay = NextBetween(minMilliseconds, maxMilliseconds);
        return Task.Delay(nextDelay, cancellationToken);
    }

    internal Task PauseAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return Task.CompletedTask;
        }

        return Task.Delay(milliseconds, cancellationToken);
    }

    internal int NextBetween(int minMilliseconds, int maxMilliseconds)
    {
        if (minMilliseconds < 0)
        {
            minMilliseconds = 0;
        }

        if (maxMilliseconds < minMilliseconds)
        {
            maxMilliseconds = minMilliseconds + 1;
        }

        return _random.Next(minMilliseconds, maxMilliseconds + 1);
    }
}
