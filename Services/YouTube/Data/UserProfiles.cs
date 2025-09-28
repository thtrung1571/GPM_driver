using System;
using System.Collections.Generic;
using System.Linq;

namespace GPM_driver.Services.YouTube.Data;

internal static class UserProfiles
{
    private static readonly IReadOnlyDictionary<string, YouTubePersona> Personas =
        new Dictionary<string, YouTubePersona>(StringComparer.OrdinalIgnoreCase)
        {
            ["Generalist"] = new(
                "Generalist",
                new[]
                {
                    "latest news",
                    "technology reviews",
                    "travel vlogs",
                    "music videos",
                    "productivity tips"
                },
                new[]
                {
                    "gaming highlights",
                    "movie trailers",
                    "podcast clips",
                    "cooking recipes"
                }),
            ["Creator"] = new(
                "Creator",
                new[]
                {
                    "content creation tips",
                    "video editing tutorials",
                    "camera gear reviews",
                    "productivity workflows"
                },
                new[]
                {
                    "vlog inspiration",
                    "social media strategy",
                    "motion graphics"
                }),
            ["Gamer"] = new(
                "Gamer",
                new[]
                {
                    "game walkthrough",
                    "esports highlights",
                    "speedrun",
                    "game review"
                },
                new[]
                {
                    "game soundtrack",
                    "gaming news",
                    "mod showcase"
                }),
            ["Learner"] = new(
                "Learner",
                new[]
                {
                    "programming tutorial",
                    "math lecture",
                    "science documentary",
                    "history explained"
                },
                new[]
                {
                    "study music",
                    "note taking tips",
                    "learning strategies"
                })
        };

    internal static YouTubePersona ResolvePersona(string? personaName)
    {
        if (string.IsNullOrWhiteSpace(personaName))
        {
            return Personas["Generalist"];
        }

        if (Personas.TryGetValue(personaName, out var persona))
        {
            return persona;
        }

        return Personas.Values.First();
    }
}

internal sealed record YouTubePersona(
    string Name,
    IReadOnlyList<string> PrimaryInterests,
    IReadOnlyList<string> SecondaryInterests);
