using System;
using System.Collections.Generic;
using System.Linq;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;

namespace GPM_driver.Services.YouTube.Behaviors;

internal sealed class BehaviorWeightCalculator
{
    private readonly ILogger<BehaviorWeightCalculator>? _logger;

    public BehaviorWeightCalculator(ILogger<BehaviorWeightCalculator>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<(IYouTubeWarmupBehavior Behavior, double Weight)> Calculate(
        IEnumerable<IYouTubeWarmupBehavior> registry,
        YouTubeWarmupSettings config,
        bool freshLanding)
    {
        var available = registry?.ToList() ?? new List<IYouTubeWarmupBehavior>();
        if (available.Count == 0)
        {
            _logger?.LogWarning("No YouTube warmup behaviours registered.");
            return Array.Empty<(IYouTubeWarmupBehavior, double)>();
        }

        var filtered = FilterBehaviors(available, config?.Behaviors);
        if (filtered.Count == 0)
        {
            filtered = available;
        }

        var results = new List<(IYouTubeWarmupBehavior, double)>();
        foreach (var behavior in filtered.Distinct())
        {
            double weight = GetWeightForBehavior(behavior.Kind, config, freshLanding);
            if (weight <= 0)
            {
                continue;
            }

            results.Add((behavior, weight));
        }

        if (results.Count == 0)
        {
            var fallback = available.FirstOrDefault(b => b.Kind == YouTubeWarmupBehaviorKind.Search)
                ?? available.First();
            results.Add((fallback, 1.0));
        }

        return results;
    }

    public IYouTubeWarmupBehavior? PickBehavior(
        IReadOnlyList<(IYouTubeWarmupBehavior Behavior, double Weight)> weighted,
        Random random)
    {
        if (weighted == null || weighted.Count == 0)
        {
            return null;
        }

        double total = weighted.Sum(entry => entry.Weight);
        if (total <= 0)
        {
            return weighted[^1].Behavior;
        }

        double roll = random.NextDouble() * total;
        double cumulative = 0;

        foreach (var entry in weighted)
        {
            cumulative += entry.Weight;
            if (roll <= cumulative)
            {
                return entry.Behavior;
            }
        }

        return weighted[^1].Behavior;
    }

    private static List<IYouTubeWarmupBehavior> FilterBehaviors(
        IEnumerable<IYouTubeWarmupBehavior> registry,
        string[]? requested)
    {
        if (requested == null || requested.Length == 0)
        {
            return registry.ToList();
        }

        var results = new List<IYouTubeWarmupBehavior>();
        foreach (string name in requested)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var match = registry.FirstOrDefault(behavior => behavior.Matches(name));
            if (match != null && !results.Contains(match))
            {
                results.Add(match);
            }
        }

        return results;
    }

    private static double GetWeightForBehavior(
        YouTubeWarmupBehaviorKind kind,
        YouTubeWarmupSettings config,
        bool freshLanding)
    {
        if (config == null)
        {
            return kind == YouTubeWarmupBehaviorKind.Search ? 1.0 : 0.0;
        }

        if (freshLanding)
        {
            return kind switch
            {
                YouTubeWarmupBehaviorKind.Search => 0.7,
                YouTubeWarmupBehaviorKind.Shorts => 0.3,
                YouTubeWarmupBehaviorKind.Home => 0.0,
                YouTubeWarmupBehaviorKind.Recommendations => 0.0,
                _ => 0.0
            };
        }

        return kind switch
        {
            YouTubeWarmupBehaviorKind.Search => Math.Max(0.01, config.SearchWeight),
            YouTubeWarmupBehaviorKind.Home => Math.Max(0.01, config.HomeWeight),
            YouTubeWarmupBehaviorKind.Shorts => Math.Max(0.01, config.ShortsWeight),
            YouTubeWarmupBehaviorKind.Recommendations => Math.Max(0.01, config.RecommendationsWeight),
            _ => 0.0
        };
    }
}
