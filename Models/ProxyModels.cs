using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace GPM_driver.Models
{
    public class ProxyXoayResponse
    {
        [JsonPropertyName("status")] public int status { get; set; }
        [JsonPropertyName("message")] public string message { get; set; }
        [JsonPropertyName("proxyhttp")] public string proxyhttp { get; set; }
        [JsonPropertyName("proxysocks5")] public string proxysocks5 { get; set; }

        // JSON keys with spaces -> map explicit names
        [JsonPropertyName("Nha Mang")] public string NhaMang { get; set; }
        [JsonPropertyName("Vi Tri")] public string ViTri { get; set; }
        [JsonPropertyName("Token expiration date")] public string TokenExpirationDate { get; set; }
    }
}