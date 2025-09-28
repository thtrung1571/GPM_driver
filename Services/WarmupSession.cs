using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GPM_driver.Behaviors;
using GPM_driver.Helpers;
using GPM_driver.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services;

internal class WarmupSession
{
    private readonly AppSettings _settings;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly ILogger<WarmupSession> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string? _profileId;

    public WarmupSession(
        AppSettings settings,
        IBrowser browser,
        IBrowserContext context,
        ILogger<WarmupSession> logger,
        ILoggerFactory loggerFactory,
        string? profileId)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _profileId = profileId;
    }

    public async Task RunAsync()
    {
        var page = await EnsurePrimaryPageAsync();

        await RunIpChecksAsync(page);
        await RunSmartSearchAsync(page);
        await RunGoogleWarmupAsync();
        await RunYouTubeWarmupAsync();
    }

    private async Task<IPage> EnsurePrimaryPageAsync()
    {
        var page = _context.Pages.FirstOrDefault(p => !p.IsClosed);
        if (page != null)
        {
            return page;
        }

        _logger.LogDebug("No existing page available, opening a new tab for warmup.");
        page = await _context.NewPageAsync();
        return page;
    }

    private async Task RunIpChecksAsync(IPage page)
    {
        try
        {
            var ipHeyLogger = _loggerFactory.CreateLogger<IpHeyService>();
            var ipHeyService = new IpHeyService(page, ipHeyLogger);
            var ipHeyResult = await ipHeyService.CheckAsync();

            _logger.LogInformation(
                "IpHey status={Status} browser={Browser} location={Location} ip={Ip}",
                ipHeyResult.Status,
                ipHeyResult.Browser,
                ipHeyResult.Location,
                ipHeyResult.Ip);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IpHey warmup check failed.");
        }

        try
        {
            var ipFighterLogger = _loggerFactory.CreateLogger<IpFighterService>();
            var ipFighterService = new IpFighterService(page, ipFighterLogger);
            var ipResult = await ipFighterService.CheckIpAsync();

            _logger.LogInformation(
                "IpFighter isp={Isp} blacklist={Blacklist} proxy={Proxy} score={Score}",
                ipResult.Isp,
                ipResult.Blacklist,
                ipResult.Proxy,
                ipResult.Score);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IpFighter warmup check failed.");
        }
    }

    private async Task RunSmartSearchAsync(IPage page)
    {
        var keywordDirectory = _settings.Search.SmartSearchKeywordDirectory;
        if (string.IsNullOrWhiteSpace(keywordDirectory))
        {
            _logger.LogInformation("SmartSearch keyword directory not configured. Skipping SmartSearch warmup.");
            return;
        }

        if (!Directory.Exists(keywordDirectory))
        {
            _logger.LogWarning("SmartSearch keyword directory '{Directory}' does not exist.", keywordDirectory);
            return;
        }

        try
        {
            var smartSearch = new SmartSearchService(page, _browser, keywordDirectory);
            await smartSearch.SearchOneRandomKeywordAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SmartSearch warmup failed.");
        }
    }

    private async Task RunGoogleWarmupAsync()
    {
        var keywordDirectory = _settings.Search.GoogleKeywordDirectory;
        if (string.IsNullOrWhiteSpace(keywordDirectory))
        {
            _logger.LogInformation("Google keyword directory not configured. Skipping Google warmup.");
            return;
        }

        if (!Directory.Exists(keywordDirectory))
        {
            _logger.LogWarning("Google keyword directory '{Directory}' does not exist.", keywordDirectory);
            return;
        }

        var warmupConfig = _settings.Search.GoogleWarmup ?? new GoogleWarmupSettings();
        if (warmupConfig.MinSearches < 1)
        {
            warmupConfig.MinSearches = 1;
        }
        if (warmupConfig.MaxSearches < warmupConfig.MinSearches)
        {
            warmupConfig.MaxSearches = warmupConfig.MinSearches;
        }

        var googleLogger = _loggerFactory.CreateLogger<GoogleSearchService>();
        var googleSearch = new GoogleSearchService(_context, googleLogger);

        try
        {
            await googleSearch.RunWarmupAsync(
                keywordDirectory,
                warmupConfig,
                async visitedPage =>
                {
                    if (visitedPage == null)
                    {
                        return;
                    }

                    var userBehavior = new UserBehavior(visitedPage);
                    await userBehavior.RunAsync();
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google warmup failed.");
        }
    }

    private async Task RunYouTubeWarmupAsync()
    {
        var warmupConfig = _settings.Search.YouTubeWarmup;
        if (warmupConfig == null || !warmupConfig.Enabled)
        {
            _logger.LogInformation("YouTube warmup disabled or not configured. Skipping.");
            return;
        }

        var keywordDirectory = _settings.Search.YouTubeKeywordDirectory;
        if (!string.IsNullOrWhiteSpace(keywordDirectory) && !Directory.Exists(keywordDirectory))
        {
            _logger.LogWarning("YouTube keyword directory '{Directory}' does not exist. Falling back to built-in keywords.", keywordDirectory);
            keywordDirectory = null;
        }

        var ytLogger = _loggerFactory.CreateLogger<YouTubeWarmupService>();
        var youtubeWarmup = new YouTubeWarmupService(_context, ytLogger);

        try
        {
            await youtubeWarmup.RunWarmupAsync(keywordDirectory, warmupConfig, _profileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube warmup failed.");
        }
    }
}
