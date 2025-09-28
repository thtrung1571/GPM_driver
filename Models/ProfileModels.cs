using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace GPM_driver.Models
{
    public class CreateProfileRequest
    {
        [JsonPropertyName("profile_name")] public string ProfileName { get; set; } = string.Empty;
        [JsonPropertyName("browser_core")] public string BrowserCore { get; set; } = "chromium";
        [JsonPropertyName("browser_name")] public string BrowserName { get; set; } = "Chrome";
        [JsonPropertyName("browser_version")] public string? BrowserVersion { get; set; }
            = null;
        [JsonPropertyName("is_random_browser_version")] public bool IsRandomBrowserVersion { get; set; }
            = false;
        [JsonPropertyName("raw_proxy")] public string RawProxy { get; set; } = string.Empty;
        [JsonPropertyName("startup_urls")] public string StartupUrls { get; set; } = string.Empty;
        [JsonPropertyName("is_masked_font")] public bool IsMaskedFont { get; set; }
            = true;
        [JsonPropertyName("is_noise_canvas")] public bool IsNoiseCanvas { get; set; }
            = true;
        [JsonPropertyName("is_noise_webgl")] public bool IsNoiseWebgl { get; set; }
            = false;
        [JsonPropertyName("is_noise_client_rect")] public bool IsNoiseClientRect { get; set; }
            = true;
        [JsonPropertyName("is_noise_audio_context")] public bool IsNoiseAudioContext { get; set; }
            = true;
        [JsonPropertyName("is_random_screen")] public bool IsRandomScreen { get; set; }
            = true;
        [JsonPropertyName("is_masked_webgl_data")] public bool IsMaskedWebglData { get; set; }
            = true;
        [JsonPropertyName("is_masked_media_device")] public bool IsMaskedMediaDevice { get; set; }
            = true;
        [JsonPropertyName("is_random_os")] public bool IsRandomOs { get; set; } = false;
        [JsonPropertyName("os")] public string Os { get; set; } = "Windows 10";
        [JsonPropertyName("webrtc_mode")] public int WebrtcMode { get; set; } = 2;
    }

    public class CreateProfileData
    {
        [JsonPropertyName("id")] public string id { get; set; }
        [JsonPropertyName("name")] public string name { get; set; }
        [JsonPropertyName("raw_proxy")] public string raw_proxy { get; set; }
        [JsonPropertyName("profile_path")] public string profile_path { get; set; }
        [JsonPropertyName("browser_type")] public string browser_type { get; set; }
        [JsonPropertyName("browser_version")] public string browser_version { get; set; }
        [JsonPropertyName("note")] public string note { get; set; }
        [JsonPropertyName("group_id")] public int group_id { get; set; }
        [JsonPropertyName("created_at")] public string created_at { get; set; }
    }

    public class CreateProfileResponse
    {
        [JsonPropertyName("success")] public bool success { get; set; }
        [JsonPropertyName("data")] public CreateProfileData data { get; set; }
        [JsonPropertyName("message")] public string message { get; set; }
    }

    public class StartProfileData
    {
        [JsonPropertyName("profile_id")] public string profile_id { get; set; }
        [JsonPropertyName("browser_location")] public string browser_location { get; set; }
        [JsonPropertyName("remote_debugging_address")] public string remote_debugging_address { get; set; }
        [JsonPropertyName("driver_path")] public string driver_path { get; set; }
        [JsonPropertyName("process_id")] public int process_id { get; set; }
    }

    public class StartProfileResponse
    {
        [JsonPropertyName("success")] public bool success { get; set; }
        [JsonPropertyName("data")] public StartProfileData data { get; set; }
        [JsonPropertyName("message")] public string message { get; set; }
    }
}