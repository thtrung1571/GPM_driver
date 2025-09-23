using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

class Program
{
    static async Task Main()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.Get<AppSettings>() ?? throw new InvalidOperationException("Configuration could not be loaded.");

        if (string.IsNullOrWhiteSpace(settings.Gpm.BaseUrl))
        {
            throw new InvalidOperationException("GPM base URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.Gpm.ProxyApiUrl))
        {
            throw new InvalidOperationException("Proxy API URL is not configured.");
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "HH:mm:ss ";
                    options.SingleLine = true;
                });
        });

        var logger = loggerFactory.CreateLogger<Program>();
        logger.LogInformation("Starting GPM driver with profile template '{ProfileName}'.", settings.Gpm.Profile.ProfileName);

        using var gpm = new GPM_API(settings.Gpm.BaseUrl);

        int apiRetries = Math.Max(1, settings.Gpm.ApiRetryAttempts);
        var initialDelay = TimeSpan.FromMilliseconds(Math.Max(0, settings.Gpm.ApiRetryInitialDelayMs));
        var configuredMaxDelayMs = Math.Max(0, settings.Gpm.ApiRetryMaxDelayMs);
        var maxDelay = TimeSpan.FromMilliseconds(Math.Max(initialDelay.TotalMilliseconds, configuredMaxDelayMs));
        if (maxDelay == TimeSpan.Zero)
        {
            maxDelay = TimeSpan.FromMilliseconds(1000);
        }

        string? profileId = null;
        StartProfileResponse? startResponse = null;

        try
        {
            logger.LogInformation("Fetching rotating proxy from {ProxyEndpoint}.", settings.Gpm.ProxyApiUrl);
            ProxyXoayResponse proxy = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.FetchRotatingProxyAsync(settings.Gpm.ProxyApiUrl),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "ProxyFetch");
            logger.LogInformation("Proxy obtained. HTTP={HttpProxy} SOCKS5={SocksProxy} Message={Message}.", proxy.proxyhttp, proxy.proxysocks5, proxy.message);

            var profileTemplate = settings.Gpm.Profile;
            var osOptions = profileTemplate.OperatingSystems?.Length > 0
                ? profileTemplate.OperatingSystems
                : new[] { profileTemplate.Os };
            var selectedOs = osOptions[RandomProvider.Next(0, osOptions.Length)];

            var profileRequest = new CreateProfileRequest
            {
                ProfileName = profileTemplate.ProfileName,
                BrowserCore = profileTemplate.BrowserCore,
                BrowserName = profileTemplate.BrowserName,
                IsRandomBrowserVersion = profileTemplate.IsRandomBrowserVersion,
                RawProxy = proxy.proxyhttp,
                StartupUrls = profileTemplate.StartupUrls,
                IsMaskedFont = profileTemplate.IsMaskedFont,
                IsNoiseCanvas = profileTemplate.IsNoiseCanvas,
                IsNoiseWebgl = profileTemplate.IsNoiseWebgl,
                IsNoiseClientRect = profileTemplate.IsNoiseClientRect,
                IsNoiseAudioContext = profileTemplate.IsNoiseAudioContext,
                IsRandomScreen = profileTemplate.IsRandomScreen,
                IsMaskedWebglData = profileTemplate.IsMaskedWebglData,
                IsMaskedMediaDevice = profileTemplate.IsMaskedMediaDevice,
                IsRandomOs = profileTemplate.IsRandomOs,
                Os = selectedOs,
                WebrtcMode = profileTemplate.WebrtcMode
            };

            logger.LogInformation("Creating profile '{ProfileName}' using OS '{SelectedOs}'.", profileRequest.ProfileName, selectedOs);
            CreateProfileResponse createResp = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.CreateProfileAsync(profileRequest),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "CreateProfile");

            profileId = createResp?.data?.id;
            string? browserVersion = createResp?.data?.browser_version;
            logger.LogInformation("Profile created with id {ProfileId} and browser version {BrowserVersion}.", profileId, browserVersion ?? "unknown");

            if (string.IsNullOrEmpty(profileId))
            {
                logger.LogError("Failed to create profile or id missing. Aborting execution.");
                return;
            }

            startResponse = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.StartProfileAsync(profileId),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "StartProfile");

            if (startResponse?.data == null)
            {
                logger.LogError("Failed to start profile; start response data was null.");
                return;
            }

            logger.LogInformation("Profile started. Driver path={DriverPath}, remote debugging={RemoteDebugging}.", startResponse.data.driver_path, startResponse.data.remote_debugging_address);

            using var playwright = await Playwright.CreateAsync();
            var browser = await PlaywrightHelper.ConnectWithRetryAsync(
                playwright,
                startResponse.data.remote_debugging_address,
                logger: loggerFactory.CreateLogger("PlaywrightConnection"));
            var context = browser.Contexts.Count > 0 ? browser.Contexts[0] : await browser.NewContextAsync();
            logger.LogInformation("Attached to remote browser. Context currently has {PageCount} page(s).", context.Pages?.Count ?? 0);

            var warmupLogger = loggerFactory.CreateLogger<WarmupSession>();
            var warmup = new WarmupSession(settings, browser, context, warmupLogger, loggerFactory);
            await warmup.RunAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during orchestrator run.");
            Environment.ExitCode = -1;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                if (startResponse?.data?.profile_id != null)
                {
                    try
                    {
                        await gpm.StopProfileAsync(profileId);
                        logger.LogInformation("Stopped profile {ProfileId}.", profileId);
                    }
                    catch (Exception stopEx)
                    {
                        logger.LogWarning(stopEx, "Failed to stop profile {ProfileId}.", profileId);
                    }
                }

                try
                {
                    await gpm.DeleteProfileAsync(profileId);
                    logger.LogInformation("Deleted profile {ProfileId}.", profileId);
                }
                catch (Exception deleteEx)
                {
                    logger.LogWarning(deleteEx, "Failed to delete profile {ProfileId}.", profileId);
                }
            }
        }
    }
}
