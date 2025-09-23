namespace GPM_driver.Models;

public class AppSettings
{
    public GpmSettings Gpm { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
}

public class GpmSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ProxyApiUrl { get; set; } = string.Empty;
    public int ApiRetryAttempts { get; set; } = 3;
    public int ApiRetryInitialDelayMs { get; set; } = 500;
    public int ApiRetryMaxDelayMs { get; set; } = 8000;
    public ProfileTemplate Profile { get; set; } = new();
}

public class ProfileTemplate
{
    public string ProfileName { get; set; } = "Test profile";
    public string BrowserCore { get; set; } = "chromium";
    public string BrowserName { get; set; } = "Chrome";
    public bool IsRandomBrowserVersion { get; set; }
        = false;
    public string StartupUrls { get; set; } = string.Empty;
    public bool IsMaskedFont { get; set; } = true;
    public bool IsNoiseCanvas { get; set; } = true;
    public bool IsNoiseWebgl { get; set; } = false;
    public bool IsNoiseClientRect { get; set; } = true;
    public bool IsNoiseAudioContext { get; set; } = true;
    public bool IsRandomScreen { get; set; } = true;
    public bool IsMaskedWebglData { get; set; } = true;
    public bool IsMaskedMediaDevice { get; set; } = true;
    public bool IsRandomOs { get; set; }
        = false;
    public string Os { get; set; } = "Windows 10";
    public string[] OperatingSystems { get; set; } = new[] { "Windows 10" };
    public int WebrtcMode { get; set; } = 2;
}

public class SearchSettings
{
    public string SmartSearchKeywordDirectory { get; set; } = string.Empty;
    public string GoogleKeywordDirectory { get; set; } = string.Empty;
    public GoogleWarmupSettings GoogleWarmup { get; set; } = new();
}

public class GoogleWarmupSettings
{
    public int MinSearches { get; set; } = 1;
    public int MaxSearches { get; set; } = 3;
    public double ContinueProbability { get; set; } = 0.5;
    public int MaxBacktracks { get; set; } = 2;
    public string[] Domains { get; set; } = new[] { "https://www.google.com", "https://www.google.com.vn" };
}
