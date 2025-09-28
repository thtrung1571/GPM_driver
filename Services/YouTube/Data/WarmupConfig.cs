using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using GPM_driver.Models;

namespace GPM_driver.Services.YouTube.Data;

internal sealed class YouTubeWarmupConfiguration
{
    internal YouTubeWarmupConfiguration(
        YouTubeWarmupSettings settings,
        YouTubePersona persona,
        IReadOnlyCollection<string> keywords,
        IReadOnlyCollection<string> behaviors,
        string startDomain)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Persona = persona ?? throw new ArgumentNullException(nameof(persona));
        Keywords = new ReadOnlyCollection<string>(keywords?.ToArray() ?? Array.Empty<string>());
        Behaviors = new ReadOnlyCollection<string>(behaviors?.ToArray() ?? Array.Empty<string>());
        StartDomain = startDomain;
    }

    internal YouTubeWarmupSettings Settings { get; }

    internal YouTubePersona Persona { get; }

    internal IReadOnlyList<string> Keywords { get; }

    internal IReadOnlyList<string> Behaviors { get; }

    internal string StartDomain { get; }

    internal string GetRandomKeyword(Random random)
    {
        if (Keywords.Count == 0)
        {
            return "popular videos";
        }

        var index = random.Next(Keywords.Count);
        return Keywords[index];
    }
}

internal static class YouTubeWarmupBehavior
{
    internal const string Search = "Search";
    internal const string Home = "Home";
    internal const string Shorts = "Shorts";
    internal const string Recommendations = "Recommendations";
}
