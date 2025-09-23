using GPM_driver.Behaviors;
using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services;
using Microsoft.Extensions.Configuration;
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
            // 1) Fetch rotating proxy with retries
            ProxyXoayResponse proxy = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.FetchRotatingProxyAsync(settings.Gpm.ProxyApiUrl),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "ProxyFetch");
            Console.WriteLine($"HTTP: {proxy.proxyhttp}\nSOCKS5: {proxy.proxysocks5}\nMessage: {proxy.message}");

            // 2) Create profile with fetched proxy
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

            CreateProfileResponse createResp = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.CreateProfileAsync(profileRequest),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "CreateProfile");

            profileId = createResp?.data?.id;
            string? browserVersion = createResp?.data?.browser_version;
            Console.WriteLine($"Created profile id: {profileId}");
            Console.WriteLine($"Browser version: {browserVersion}");

            if (string.IsNullOrEmpty(profileId))
            {
                Console.WriteLine("Failed to create profile or id missing.");
                return;
            }

            // 3) Start profile
            startResponse = await RetryHelper.ExecuteWithRetryAsync(
                () => gpm.StartProfileAsync(profileId),
                maxAttempts: apiRetries,
                initialDelay: initialDelay,
                maxDelay: maxDelay,
                operationName: "StartProfile");

            if (startResponse?.data == null)
            {
                Console.WriteLine("Failed to start profile; start response data was null.");
                return;
            }

            Console.WriteLine($"Driver path: {startResponse.data.driver_path}");
            Console.WriteLine($"Remote debug address: {startResponse.data.remote_debugging_address}");

            // 4) Connect Playwright to remote debugging
            using var playwright = await Playwright.CreateAsync();
            var browser = await PlaywrightHelper.ConnectWithRetryAsync(playwright, startResponse.data.remote_debugging_address);
            var context = browser.Contexts.Count > 0 ? browser.Contexts[0] : await browser.NewContextAsync();
            var pages = context.Pages;
            var page = pages.Count > 0 ? pages[0] : await context.NewPageAsync();

            //5) Use IpHeyService
            try
            {
                var ipHeyService = new IpHeyService(page);
                IpHeyResult ipHeyResult = await ipHeyService.CheckAsync();
                Console.WriteLine($"Status: {ipHeyResult.Status}");
                Console.WriteLine($"Browser: {ipHeyResult.Browser}");
                Console.WriteLine($"Location: {ipHeyResult.Location}");
                Console.WriteLine($"IP: {ipHeyResult.Ip}");
                Console.WriteLine($"Hardware: {ipHeyResult.Hardware}");
                Console.WriteLine($"Software: {ipHeyResult.Software}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IpHey] Failed to complete check: {ex.Message}");
            }

            //6) Use IpFighterService
            try
            {
                var ipFighter = new IpFighterService(page);
                IpFighterResult result = await ipFighter.CheckIpAsync();
                Console.WriteLine($"ISP: {result.Isp}");
                Console.WriteLine($"Blacklist: {result.Blacklist}");
                Console.WriteLine($"Proxy: {result.Proxy}");
                Console.WriteLine($"WebRTC: {result.WebRTC}");
                Console.WriteLine($"Score: {result.Score}");
                Console.WriteLine($"City: {result.City}");
                Console.WriteLine($"Country: {result.Country}");
                Console.WriteLine($"Hostname: {result.Hostname}");
                Console.WriteLine($"DNS: {result.DNS}");
                Console.WriteLine($"Blacklist Details: {result.BlacklistDetails}");
                Console.WriteLine($"Blacklist Servers: {result.BlacklistServers}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IpFighter] Failed to complete check: {ex.Message}");
            }

            // --- 7) Use SmartSearch ---
            if (!string.IsNullOrWhiteSpace(settings.Search.SmartSearchKeywordDirectory))
            {
                try
                {
                    var smartSearch = new SmartSearchService(page, browser, settings.Search.SmartSearchKeywordDirectory);
                    await smartSearch.SearchOneRandomKeywordAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SmartSearch] Failed: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("SmartSearch keyword directory not configured. Skipping SmartSearchService.");
            }

            // --- 8) Use GoogleSearchService ---
            if (!string.IsNullOrWhiteSpace(settings.Search.GoogleKeywordDirectory))
            {
                try
                {
                    var googleSearch = new GoogleSearchService(context);
                    var currentPage = await googleSearch.SearchOneRandomKeywordAsync(settings.Search.GoogleKeywordDirectory);
                    var userBehavior = new UserBehavior(currentPage);
                    await userBehavior.RunAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GoogleSearch] Failed: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Google keyword directory not configured. Skipping GoogleSearchService.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled error: {ex.Message}\n{ex}");
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
                        Console.WriteLine($"Stopped profile {profileId}.");
                    }
                    catch (Exception stopEx)
                    {
                        Console.Error.WriteLine($"Failed to stop profile {profileId}: {stopEx.Message}");
                    }
                }

                try
                {
                    await gpm.DeleteProfileAsync(profileId);
                    Console.WriteLine($"Deleted profile {profileId}.");
                }
                catch (Exception deleteEx)
                {
                    Console.Error.WriteLine($"Failed to delete profile {profileId}: {deleteEx.Message}");
                }
            }
        }
    }
}
