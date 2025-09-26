using System;

namespace GPM_driver.Helpers;

internal static class YouTubeUrlHelper
{
    internal enum YouTubeVideoKind
    {
        Unknown,
        Video,
        Playlist,
        Short,
        Live
    }

    internal static YouTubeVideoKind GetVideoKind(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return YouTubeVideoKind.Unknown;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return YouTubeVideoKind.Unknown;
        }

        string path = uri.AbsolutePath ?? string.Empty;
        path = path.TrimEnd('/');

        if (path.Contains("/shorts", StringComparison.OrdinalIgnoreCase))
        {
            return YouTubeVideoKind.Short;
        }

        if (path.Contains("/live", StringComparison.OrdinalIgnoreCase))
        {
            return YouTubeVideoKind.Live;
        }

        if (path.Contains("/playlist", StringComparison.OrdinalIgnoreCase))
        {
            return YouTubeVideoKind.Playlist;
        }

        if (path.Contains("/watch", StringComparison.OrdinalIgnoreCase))
        {
            if (HasQueryParameter(uri, "list"))
            {
                return YouTubeVideoKind.Playlist;
            }

            if (HasQueryParameter(uri, "v"))
            {
                return YouTubeVideoKind.Video;
            }
        }

        if (path.Contains("/embed", StringComparison.OrdinalIgnoreCase))
        {
            return YouTubeVideoKind.Video;
        }

        return YouTubeVideoKind.Video;
    }

    internal static bool IsShort(string? url) => GetVideoKind(url) == YouTubeVideoKind.Short;

    private static bool HasQueryParameter(Uri uri, string name)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return false;
        }

        var segments = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var parts = segment.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return parts.Length == 1 || !string.IsNullOrEmpty(parts[1]);
            }
        }

        return false;
    }
}
