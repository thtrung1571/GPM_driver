using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GPM_driver.Models;

namespace GPM_driver.Services
{
    public class GPM_API : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // baseUrl e.g. "http://127.0.0.1:19995"
        public GPM_API(string baseUrl)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')) };
        }

        public async Task<CreateProfileResponse> CreateProfileAsync(object profilePayload)
        {
            string json = JsonSerializer.Serialize(profilePayload, _jsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync("/api/v3/profiles/create", content);
            string respJson = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<CreateProfileResponse>(respJson, _jsonOpts);
        }

        // proxyUrl can be absolute (https://proxyxoay.shop/...)
        public async Task<ProxyXoayResponse> FetchRotatingProxyAsync(string proxyUrl)
        {
            var resp = await _httpClient.GetAsync(proxyUrl);
            string json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<ProxyXoayResponse>(json, _jsonOpts);
        }

        public async Task<StartProfileResponse> StartProfileAsync(string profileId)
        {
            var resp = await _httpClient.GetAsync($"/api/v3/profiles/start/{profileId}");
            string json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<StartProfileResponse>(json, _jsonOpts);
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}
