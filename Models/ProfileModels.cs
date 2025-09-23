using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace GPM_driver.Models
{
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