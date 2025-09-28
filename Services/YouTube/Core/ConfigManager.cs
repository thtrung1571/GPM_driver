using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using GPM_driver.Models;
using GPM_driver.Services.YouTube.Data;
using Microsoft.Extensions.Logging;

namespace GPM_driver.Services.YouTube.Core;

internal sealed class ConfigManager
{
    private readonly ILogger _logger;
    private readonly Random _random;

    internal ConfigManager(ILogger logger, Random random)
    {
        _logger = logger;
        _random = random;
    }

    internal async Task<YouTubeWarmupConfiguration> LoadAsync(
        string? keywordDirectory,
        YouTubeWarmupSettings settings,
        string? profileId,
        CancellationToken cancellationToken)
    {
        var persona = UserProfiles.ResolvePersona(settings.Persona);
        var keywords = await LoadKeywordsAsync(keywordDirectory, persona, cancellationToken);
        var behaviors = settings.Behaviors?.Length > 0 ? settings.Behaviors : new[] { YouTubeWarmupBehavior.Search, YouTubeWarmupBehavior.Home };
        var domain = ResolveDomain(settings);

        var configuration = new YouTubeWarmupConfiguration(
            settings,
            persona,
            keywords,
            behaviors,
            domain);

        await CacheIdentityAsync(configuration, settings, profileId, cancellationToken);
        return configuration;
    }

    private async Task<IReadOnlyCollection<string>> LoadKeywordsAsync(
        string? keywordDirectory,
        YouTubePersona persona,
        CancellationToken cancellationToken)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(keywordDirectory) && Directory.Exists(keywordDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(keywordDirectory, "*.txt", SearchOption.AllDirectories))
            {
                await foreach (var keyword in ReadKeywordsAsync(file, cancellationToken))
                {
                    keywords.Add(keyword);
                }
            }
        }

        if (keywords.Count == 0)
        {
            foreach (var keyword in persona.PrimaryInterests.Concat(persona.SecondaryInterests))
            {
                keywords.Add(keyword);
            }
        }

        return keywords.ToArray();
    }

    private static async IAsyncEnumerable<string> ReadKeywordsAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return line.Trim();
        }
    }

    private static string ResolveDomain(YouTubeWarmupSettings settings)
    {
        var domains = settings.Domains ?? Array.Empty<string>();
        if (domains.Length == 0)
        {
            return "https://www.youtube.com";
        }

        return domains[0];
    }

    private async Task CacheIdentityAsync(
        YouTubeWarmupConfiguration configuration,
        YouTubeWarmupSettings settings,
        string? profileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.IdentityCacheDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(settings.IdentityCacheDirectory);
            var safeProfile = string.IsNullOrWhiteSpace(profileId) ? "default" : profileId;
            var path = Path.Combine(settings.IdentityCacheDirectory, $"{safeProfile}.json");
            var cache = new IdentityCache
            {
                Persona = configuration.Persona.Name,
                LastKeywords = configuration.Keywords.OrderBy(_ => _random.Next()).Take(settings.IdentityKeywordFileCount).ToArray(),
                Timestamp = DateTimeOffset.UtcNow
            };

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, cache, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist YouTube warmup identity cache.");
        }
    }

    private sealed record IdentityCache
    {
        public string Persona { get; init; } = string.Empty;
        public string[] LastKeywords { get; init; } = Array.Empty<string>();
        public DateTimeOffset Timestamp { get; init; }
    }
}
