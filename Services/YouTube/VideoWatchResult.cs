using System;
using GPM_driver.Helpers;

namespace GPM_driver.Services.YouTube;

internal sealed class VideoWatchResult
{
    public string Context { get; set; } = string.Empty;
    public string ContextDetail { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int? ResultPosition { get; set; }
        = null;
    public string? Keyword { get; set; }
        = null;
    public string? Url { get; set; }
        = string.Empty;
    public string? Title { get; set; }
        = string.Empty;
    public string? ChannelName { get; set; }
        = string.Empty;
    public int PlannedWatchDurationMs { get; set; }
        = 0;
    public int ActualWatchDurationMs { get; set; }
        = 0;
    public DateTimeOffset StartedAt { get; set; }
        = DateTimeOffset.UtcNow;
    public bool IsShort { get; set; }
        = false;
    public string? ParentVideoUrl { get; set; }
        = null;
    public string? ParentVideoTitle { get; set; }
        = null;
    public string? ParentContext { get; set; }
        = null;
    public YouTubeUrlHelper.YouTubeVideoKind VideoType { get; set; }
        = YouTubeUrlHelper.YouTubeVideoKind.Unknown;
    public int? VolumePercent { get; set; }
        = null;
}
