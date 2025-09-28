using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GPM_driver.Models;

namespace GPM_driver.Services;

public class GPM_API : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public GPM_API(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')) };
    }

    public async Task<CreateProfileResponse> CreateProfileAsync(CreateProfileRequest profilePayload)
    {
        string json = JsonSerializer.Serialize(profilePayload, _jsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync("/api/v3/profiles/create", content);
        string respJson = await resp.Content.ReadAsStringAsync();
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<CreateProfileResponse>(respJson, _jsonOpts)!;
    }

    public async Task<ProxyXoayResponse> FetchRotatingProxyAsync(string proxyUrl)
    {
        var resp = await _httpClient.GetAsync(proxyUrl);
        string json = await resp.Content.ReadAsStringAsync();
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<ProxyXoayResponse>(json, _jsonOpts)!;
    }

    public async Task<StartProfileResponse> StartProfileAsync(string profileId)
    {
        var resp = await _httpClient.GetAsync($"/api/v3/profiles/start/{profileId}");
        string json = await resp.Content.ReadAsStringAsync();
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<StartProfileResponse>(json, _jsonOpts)!;
    }

    public async Task StopProfileAsync(string profileId)
    {
        var resp = await _httpClient.GetAsync($"/api/v3/profiles/stop/{profileId}");
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        var resp = await _httpClient.DeleteAsync($"/api/v3/profiles/delete/{profileId}");
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose() => _httpClient.Dispose();
}
