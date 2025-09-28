using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Activities;
using GPM_driver.Services.YouTube.Data;

namespace GPM_driver.Services.YouTube.Core;

internal sealed class WarmupManager
{
    private readonly WarmupContext _context;
    private readonly IDictionary<string, BaseActivity> _activities;
    private readonly Random _random;

    internal WarmupManager(WarmupContext context, IEnumerable<BaseActivity> activities, Random random)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _activities = new Dictionary<string, BaseActivity>(StringComparer.OrdinalIgnoreCase);

        foreach (var activity in activities)
        {
            _activities[activity.Name] = activity;
        }
    }

    internal async Task RunAsync()
    {
        var settings = _context.Configuration.Settings;
        int plannedInteractions = _random.Next(settings.MinInteractions, settings.MaxInteractions + 1);
        int executedInteractions = 0;

        while (executedInteractions < plannedInteractions)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            await _context.RateLimiter.WaitForTurnAsync(settings.MinDelayBetweenActionsMs, settings.MaxDelayBetweenActionsMs, _context.CancellationToken);

            var behavior = _context.BehaviorPattern.ChooseBehavior(_context.Configuration.Behaviors, settings);
            if (!_activities.TryGetValue(behavior, out var activity))
            {
                _context.Logger.LogDebug("No YouTube warmup activity mapped for behavior {Behavior}.", behavior);
                return;
            }

            bool executed = await activity.TryExecuteAsync(_context);
            if (!executed)
            {
                _context.Logger.LogDebug("YouTube warmup activity {Activity} did not complete successfully.", activity.Name);
            }
            else
            {
                executedInteractions++;
            }

            if (executedInteractions >= settings.MaxInteractions)
            {
                break;
            }

            if (_random.NextDouble() > settings.ContinueProbability)
            {
                break;
            }
        }

        _context.Logger.LogInformation("Completed {Count} YouTube warmup interactions.", executedInteractions);
    }
}
