using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Models;

namespace GPM_driver.Services.YouTube.Behaviors;

internal enum YouTubeWarmupBehaviorKind
{
    Search,
    Home,
    Shorts,
    Recommendations
}

internal interface IYouTubeWarmupBehavior
{
    string Name { get; }

    YouTubeWarmupBehaviorKind Kind { get; }

    IEnumerable<string> Aliases { get; }

    bool Matches(string identifier);

    Task<bool> ExecuteAsync(WarmupContext context, YouTubeWarmupSettings config, CancellationToken token);
}
