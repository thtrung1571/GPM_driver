using System;
using System.Threading;
using System.Threading.Tasks;

using GPM_driver.Helpers;

namespace GPM_driver.Services.YouTube.Safety;

internal sealed class DetectionAvoidance
{
    private readonly Random _random;

    internal DetectionAvoidance(Random random)
    {
        _random = random;
    }

    internal async Task RandomMouseJitterAsync(MouseHelper mouse, CancellationToken cancellationToken)
    {
        if (mouse == null)
        {
            return;
        }

        var jitterCount = _random.Next(1, 3);
        for (int i = 0; i < jitterCount; i++)
        {
            var viewport = mouse.Page.ViewportSize;
            int width = viewport?.Width ?? 1280;
            int height = viewport?.Height ?? 720;
            var x = _random.Next(width / 4, width - width / 4);
            var y = _random.Next(height / 4, height - height / 4);
            await mouse.MoveAsync(x, y, steps: _random.Next(8, 15));
            await Task.Delay(_random.Next(120, 260), cancellationToken);
        }
    }

    internal Task SmallPauseAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(_random.Next(350, 900), cancellationToken);
    }
}
