using GPM_driver.Models;
using GPM_driver.Services;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Behaviors;

class Program
{

    static async Task Main()
    {
        string gpmBase = "http://127.0.0.1:19995";
        string proxyUrl = "https://proxyxoay.shop/api/get.php?key=BnQqcrAVCtRAauUjUUXyXe&&nhamang=random&&tinhthanh=0";

        using var gpm = new GPM_API(gpmBase);


        // 1) Fetch rotating proxy
        ProxyXoayResponse proxy = await gpm.FetchRotatingProxyAsync(proxyUrl);
        Console.WriteLine($"HTTP: {proxy.proxyhttp}\nSOCKS5: {proxy.proxysocks5}\nMessage: {proxy.message}");

        // 2) Create profile with fetched proxy
        var random1 = new Random();
        var osVersions = new[] { "Windows 10", "Windows 11" };
        var selectedOs = osVersions[random1.Next(osVersions.Length)];
        var profilePayload = new
        {
            profile_name = "Test profile",
            browser_core = "chromium",
            browser_name = "Chrome",
            //browser_version = "139.0.7258.139",
            is_random_browser_version = false,
            raw_proxy = proxy.proxyhttp,   // inject fetched proxy
            startup_urls = "",
            is_masked_font = true,
            is_noise_canvas = true,
            is_noise_webgl = false,
            is_noise_client_rect = true,
            is_noise_audio_context = true,
            is_random_screen = true,
            is_masked_webgl_data = true,
            is_masked_media_device = true,
            is_random_os = false,
            os = "Windows 10",
            webrtc_mode = 2
        };

        CreateProfileResponse createResp = await gpm.CreateProfileAsync(profilePayload);
        string profileId = createResp?.data?.id;
        string browser_version = createResp?.data?.browser_version;
        Console.WriteLine($"Created profile id: {profileId}");
        Console.WriteLine($"Browser version: {browser_version}");

        if (string.IsNullOrEmpty(profileId))
        {
            Console.WriteLine("Failed to create profile or id missing.");
            return;
        }

        // 3) Start profile
        StartProfileResponse startResp = await gpm.StartProfileAsync(profileId);
        Console.WriteLine($"Driver path: {startResp.data.driver_path}");
        Console.WriteLine($"Remote debug address: {startResp.data.remote_debugging_address}");

        // 4) Connect Playwright to remote debugging
        using var playwright = await Playwright.CreateAsync();
        var random = new Random();
        var delayMilliseconds = random.Next(1500, 3001); // 5000ms to 10000ms (5-10 seconds)
        //var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://{startResp.data.remote_debugging_address}");
        var browser = await PlaywrightHelper.ConnectWithRetryAsync(playwright, startResp.data.remote_debugging_address);
        var context = browser.Contexts[0];
        // Get the first available page instead of creating a new one
        var pages = context.Pages;
        var page = pages.Count > 0 ? pages[0] : await context.NewPageAsync();
        //5) Use IpHeyService
        var ipHeyService = new IpHeyService(page);
        IpHeyResult ipHeyResult = await ipHeyService.CheckAsync();
        Console.WriteLine($"Status: {ipHeyResult.Status}");
        Console.WriteLine($"Browser: {ipHeyResult.Browser}");
        Console.WriteLine($"Location: {ipHeyResult.Location}");
        Console.WriteLine($"IP: {ipHeyResult.Ip}");
        Console.WriteLine($"Hardware: {ipHeyResult.Hardware}");
        Console.WriteLine($"Software: {ipHeyResult.Software}");

        //6) Use IpFighterService
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
        // --- 7) Use SmartSearch ---
        var keywordFolder1 = @"E:\Google_Farm\google"; // path to your keyword text files
        var smartSearch = new SmartSearchService(page, browser, keywordFolder1);

        // Start searching all keywords
        await smartSearch.SearchOneRandomKeywordAsync();
        // --- 7) Use GoogleSearchService ---
        var googleSearch = new GoogleSearchService(context);
        //var keywordFolder = @"E:\Google_Farm\google"; // path to your keyword text files
        var currentPage = await googleSearch.SearchOneRandomKeywordAsync(@"E:\Google_Farm\Keyword_Search\");
        // 8) Choice of result on result page google - use the page after navigation
        var userBehavior = new GPM_driver.Behaviors.UserBehavior(currentPage);
        await userBehavior.RunAsync();

    }
}

