using System;
using System.Threading;

using GPM_driver.Helpers;
using GPM_driver.Services.YouTube.Data;
using GPM_driver.Services.YouTube.Patterns;
using GPM_driver.Services.YouTube.Safety;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Core;

internal sealed class WarmupContext
{
    internal WarmupContext(
        IBrowserContext browserContext,
        IPage page,
        MouseHelper mouse,
        KeyboardHelper keyboard,
        YouTubeWarmupConfiguration configuration,
        ActivityLog activityLog,
        TimingPattern timingPattern,
        BehaviorPattern behaviorPattern,
        NavigationPattern navigationPattern,
        RateLimiter rateLimiter,
        DetectionAvoidance detectionAvoidance,
        Random random,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        BrowserContext = browserContext ?? throw new ArgumentNullException(nameof(browserContext));
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        Keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ActivityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
        TimingPattern = timingPattern ?? throw new ArgumentNullException(nameof(timingPattern));
        BehaviorPattern = behaviorPattern ?? throw new ArgumentNullException(nameof(behaviorPattern));
        NavigationPattern = navigationPattern ?? throw new ArgumentNullException(nameof(navigationPattern));
        RateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        DetectionAvoidance = detectionAvoidance ?? throw new ArgumentNullException(nameof(detectionAvoidance));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CancellationToken = cancellationToken;
    }

    internal IBrowserContext BrowserContext { get; }

    internal IPage Page { get; }

    internal MouseHelper Mouse { get; }

    internal KeyboardHelper Keyboard { get; }

    internal YouTubeWarmupConfiguration Configuration { get; }

    internal ActivityLog ActivityLog { get; }

    internal TimingPattern TimingPattern { get; }

    internal BehaviorPattern BehaviorPattern { get; }

    internal NavigationPattern NavigationPattern { get; }

    internal RateLimiter RateLimiter { get; }

    internal DetectionAvoidance DetectionAvoidance { get; }

    internal Random Random { get; }

    internal ILogger Logger { get; }

    internal CancellationToken CancellationToken { get; }
}
