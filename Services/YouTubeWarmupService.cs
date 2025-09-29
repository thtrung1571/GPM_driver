using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;
using GPM_driver.Services.YouTube;
using GPM_driver.Services.YouTube.Behaviors;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services;

internal class YouTubeWarmupService
{
    private readonly IBrowserContext _context;
    private readonly IEnumerable<IYouTubeWarmupBehavior> _behaviors;
    private readonly BehaviorWeightCalculator _weightCalculator;
    private readonly ILogger<YouTubeWarmupService>? _logger;
    private readonly ILoggerFactory? _loggerFactory;

    public YouTubeWarmupService(
        IBrowserContext context,
        IEnumerable<IYouTubeWarmupBehavior> behaviors,
        BehaviorWeightCalculator weightCalculator,
        ILogger<YouTubeWarmupService>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _behaviors = behaviors ?? throw new ArgumentNullException(nameof(behaviors));
        _weightCalculator = weightCalculator ?? throw new ArgumentNullException(nameof(weightCalculator));
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task RunWarmupAsync(
        string? keywordDirectory,
        YouTubeWarmupSettings warmupConfig,
        string? profileId,
        CancellationToken token = default)
    {
        if (warmupConfig == null)
        {
            throw new ArgumentNullException(nameof(warmupConfig));
        }

        var warmupContext = new WarmupContext(
            _context,
            _loggerFactory?.CreateLogger<WarmupContext>(),
            _loggerFactory)
        {
            KeywordDirectory = keywordDirectory
        };

        await warmupContext.EnsurePageAsync(token);
        await warmupContext.EnsureLandingDomainAsync(warmupConfig, token);

        bool freshLanding = await warmupContext.IsFreshLandingExperienceAsync(token);
        _logger?.LogInformation(
            freshLanding
                ? "Detected fresh YouTube landing experience. Prioritising search and Shorts warmup."
                : "YouTube landing shows personalised feed. Running full warmup mix.");

        var weightedBehaviors = _weightCalculator.Calculate(_behaviors, warmupConfig, freshLanding);
        if (weightedBehaviors.Count == 0)
        {
            _logger?.LogWarning("No warmup behaviours available; skipping YouTube warmup.");
            return;
        }

        int interactions = warmupContext.Random.Next(
            Math.Max(1, warmupConfig.MinInteractions),
            Math.Max(Math.Max(1, warmupConfig.MinInteractions), warmupConfig.MaxInteractions) + 1);

        for (int i = 0; i < interactions && !token.IsCancellationRequested; i++)
        {
            var behavior = _weightCalculator.PickBehavior(weightedBehaviors, warmupContext.Random);
            if (behavior == null)
            {
                break;
            }

            _logger?.LogDebug(
                "Executing YouTube warmup behaviour {Behaviour} ({Index}/{Total}).",
                behavior.Name,
                i + 1,
                interactions);

            bool success = false;
            try
            {
                success = await behavior.ExecuteAsync(warmupContext, warmupConfig, token);
            }
            catch (PlaywrightException ex)
            {
                _logger?.LogDebug(ex, "Behaviour {Behaviour} threw a Playwright exception.", behavior.Name);
            }

            if (!success)
            {
                _logger?.LogDebug(
                    "Behaviour {Behaviour} did not complete successfully; continuing with next action.",
                    behavior.Name);
            }

            if (i < interactions - 1)
            {
                await warmupContext.DelayBetweenActionsAsync(warmupConfig, token);
            }
        }

        await warmupContext.PersistSessionStateAsync(warmupConfig, profileId, token);

        _logger?.LogInformation(
            "YouTube warmup finished with {Search} searches, {Home} home plays, {Shorts} shorts sequences, {Recommendations} recommendation hops. Visited {Videos} unique videos.",
            warmupContext.SearchInteractions,
            warmupContext.HomeInteractions,
            warmupContext.ShortsInteractions,
            warmupContext.RecommendationInteractions,
            warmupContext.VisitedVideoIds.Count);
    }
}
