using System;
using System.Collections.Generic;
using System.Linq;

using GPM_driver.Services.YouTube.Data;

namespace GPM_driver.Services.YouTube.Patterns;

internal sealed class BehaviorPattern
{
    private readonly Random _random;

    internal BehaviorPattern(Random random)
    {
        _random = random;
    }

    internal string ChooseBehavior(IReadOnlyList<string> behaviors, YouTubeWarmupSettings settings)
    {
        if (behaviors == null || behaviors.Count == 0)
        {
            return YouTubeWarmupBehavior.Search;
        }

        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [YouTubeWarmupBehavior.Search] = Math.Max(settings.SearchWeight, 0.01),
            [YouTubeWarmupBehavior.Shorts] = Math.Max(settings.ShortsWeight, 0.01),
            [YouTubeWarmupBehavior.Home] = 1.0,
            [YouTubeWarmupBehavior.Recommendations] = Math.Max(settings.RecommendationChainProbability, 0.1)
        };

        var filtered = behaviors.Where(b => weights.ContainsKey(b)).ToArray();
        if (filtered.Length == 0)
        {
            return behaviors[_random.Next(behaviors.Count)];
        }

        double total = filtered.Sum(b => weights[b]);
        var roll = _random.NextDouble() * total;
        double cumulative = 0;
        foreach (var behavior in filtered)
        {
            cumulative += weights[behavior];
            if (roll <= cumulative)
            {
                return behavior;
            }
        }

        return filtered.Last();
    }
}
