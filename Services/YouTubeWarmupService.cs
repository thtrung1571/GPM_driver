using System;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services.YouTube.Activities;
using GPM_driver.Services.YouTube.Core;
using GPM_driver.Services.YouTube.Data;
using GPM_driver.Services.YouTube.Patterns;
using GPM_driver.Services.YouTube.Safety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace GPM_driver.Services;

internal class YouTubeWarmupService
{
    private readonly IBrowserContext _context;
    private readonly ILogger<YouTubeWarmupService>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    private IPage? _page;
    private MouseHelper? _mouseHelper;
    private KeyboardHelper? _keyboardHelper;


    public YouTubeWarmupService(IBrowserContext context, ILogger<YouTubeWarmupService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task RunWarmupAsync(
        string? keywordDirectory,
        YouTubeWarmupSettings settings,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (settings.MinInteractions < 1)
        {
            settings.MinInteractions = 1;
        }

        if (settings.MaxInteractions < settings.MinInteractions)
        {
            settings.MaxInteractions = settings.MinInteractions;
        }

        if (settings.MinDelayBetweenActionsMs < 500)
        {
            settings.MinDelayBetweenActionsMs = 500;
        }

        if (settings.MaxDelayBetweenActionsMs < settings.MinDelayBetweenActionsMs)
        {
            settings.MaxDelayBetweenActionsMs = settings.MinDelayBetweenActionsMs + 500;
        }

        var logger = (ILogger)_logger ?? NullLogger<YouTubeWarmupService>.Instance;
        var sessionManager = new SessionManager(_context, logger);
        _page = await sessionManager.EnsurePageAsync(cancellationToken);
        _mouseHelper = new MouseHelper(_page);
        _keyboardHelper = new KeyboardHelper(_page);

        var timingPattern = new TimingPattern(_random);
        var behaviorPattern = new BehaviorPattern(_random);
        var navigationPattern = new NavigationPattern(_random, settings.Domains ?? Array.Empty<string>());
        var rateLimiter = new RateLimiter(_random);
        var detectionAvoidance = new DetectionAvoidance(_random);
        var activityLog = new ActivityLog();
        var configManager = new ConfigManager(logger, _random);
        var configuration = await configManager.LoadAsync(keywordDirectory, settings, profileId, cancellationToken);

        var context = new WarmupContext(
            _context,
            _page,
            _mouseHelper,
            _keyboardHelper,
            configuration,
            activityLog,
            timingPattern,
            behaviorPattern,
            navigationPattern,
            rateLimiter,
            detectionAvoidance,
            _random,
            logger,
            cancellationToken);

        var likeDislike = new LikeDislikeActivity(logger);
        var subscribe = new SubscribeActivity(logger);
        var share = new ShareActivity(logger);
        var comment = new CommentActivity(logger);
        var videoPlayer = new VideoPlayerActivity(logger, likeDislike, subscribe, share, comment);

        var activities = new BaseActivity[]
        {
            new HomeActivity(logger, videoPlayer),
            new SearchActivity(logger, videoPlayer),
            new ShortsActivity(logger),
            new RecommendationActivity(logger, videoPlayer)
        };

        var manager = new WarmupManager(context, activities, _random);
        await manager.RunAsync();

        var snapshot = string.Join(" | ", activityLog.Snapshot());
        if (!string.IsNullOrEmpty(snapshot))
        {
            logger.LogDebug("YouTube warmup log: {Log}", snapshot);
        }
    }
}
