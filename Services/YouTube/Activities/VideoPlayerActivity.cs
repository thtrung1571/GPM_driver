using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Services.YouTube;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Activities;

/// <summary>
/// Represents a warmup activity that watches the currently loaded YouTube video while interacting
/// with the player controls in a realistic manner.
/// </summary>
internal class VideoPlayerActivity
{
    private readonly IPage _page;
    private readonly PlayerControlHelper _controls;
    private readonly ILogger<VideoPlayerActivity>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    public int MinDelayBetweenActionsMs { get; set; } = 2500;
    public int MaxDelayBetweenActionsMs { get; set; } = 6000;

    public VideoPlayerActivity(IPage page, ILogger<VideoPlayerActivity>? logger = null, PlayerControlHelper? controlHelper = null)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _logger = logger;
        _controls = controlHelper ?? new PlayerControlHelper(page, logger: logger);
    }

    public async Task WatchCurrentVideoAsync(
        TimeSpan watchDuration,
        CancellationToken cancellationToken = default)
    {
        if (watchDuration <= TimeSpan.Zero)
        {
            return;
        }

        _logger?.LogInformation("Watching current YouTube video for {Duration}.", watchDuration);

        if (!await _controls.WaitForPlayerReadyAsync(cancellationToken))
        {
            _logger?.LogWarning("Timed out waiting for YouTube player to become ready.");
            return;
        }

        bool playerReady = await _controls.EnsurePlayerVisibleAsync();
        if (!playerReady)
        {
            _logger?.LogWarning("Unable to locate visible YouTube player. Skipping watch routine.");
            return;
        }

        var stopAt = DateTime.UtcNow + watchDuration;
        bool firstLoop = true;
        int actionsPerformed = 0;
        int maxInteractions = DetermineMaxInteractionsPerVideo();
        var actionCounts = new Dictionary<PlayerControlHelper.PlayerControlAction, int>();
        var failedActions = new HashSet<PlayerControlHelper.PlayerControlAction>();
        var disabledActions = SelectActionsToDisable();

        while (DateTime.UtcNow < stopAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _controls.HandleAdsAsync(cancellationToken))
            {
                // Ads consume time but we resume the loop immediately afterwards.
                continue;
            }

            if (await _controls.IsAdOverlayBlockingControlsAsync(cancellationToken))
            {
                await _controls.TrySkipAdAsync(cancellationToken);
                await _controls.DelayAsync(_random.Next(450, 850), cancellationToken);
                continue;
            }

            if (firstLoop)
            {
                await _controls.EnsureVideoPlayingAsync(cancellationToken);
                firstLoop = false;
            }

            bool interactionsAllowed = maxInteractions > 0 && actionsPerformed < maxInteractions;
            bool shouldAct = interactionsAllowed && _random.NextDouble() > 0.65;

            int delay = ComputeNextDelay();

            if (!shouldAct)
            {
                await _controls.DelayAsync(delay, cancellationToken);
                continue;
            }

            PlayerControlHelper.PlayerControlAction? action = ChooseNextAction(actionCounts, failedActions, disabledActions);

            if (action is null)
            {
                await _controls.DelayAsync(delay, cancellationToken);
                continue;
            }

            bool success = await _controls.TryPerformActionAsync(action.Value, cancellationToken);
            if (success)
            {
                actionsPerformed++;
                actionCounts[action.Value] = actionCounts.TryGetValue(action.Value, out int count) ? count + 1 : 1;
            }
            else
            {
                failedActions.Add(action.Value);
            }

            await _controls.DelayAsync(delay, cancellationToken);
        }

        await _controls.ExitFullScreenAsync(cancellationToken);
        _logger?.LogInformation("Completed watch routine on {Url}.", _page.Url);
    }

    private int ComputeNextDelay()
    {
        int min = Math.Max(1500, Math.Min(MinDelayBetweenActionsMs, MaxDelayBetweenActionsMs));
        int max = Math.Max(min + 500, Math.Max(MinDelayBetweenActionsMs, MaxDelayBetweenActionsMs));
        return _random.Next(min, max + 1);
    }

    private int DetermineMaxInteractionsPerVideo()
    {
        double roll = _random.NextDouble();
        return roll switch
        {
            < 0.35 => 0,
            < 0.7 => 1,
            < 0.9 => 2,
            _ => 3
        };
    }

    private HashSet<PlayerControlHelper.PlayerControlAction> SelectActionsToDisable()
    {
        var disabled = new HashSet<PlayerControlHelper.PlayerControlAction>();

        foreach (var action in PlayerControlHelper.DefaultActionWeights.Keys)
        {
            double chance = action switch
            {
                PlayerControlHelper.PlayerControlAction.AdjustVolumeSlider => 0.65,
                PlayerControlHelper.PlayerControlAction.AdjustVolumeKeys => 0.55,
                PlayerControlHelper.PlayerControlAction.ChangePlaybackSpeed => 0.6,
                PlayerControlHelper.PlayerControlAction.ToggleTheaterMode => 0.5,
                PlayerControlHelper.PlayerControlAction.ToggleFullScreen => 0.45,
                PlayerControlHelper.PlayerControlAction.ToggleMute => 0.45,
                PlayerControlHelper.PlayerControlAction.SeekRelative => 0.35,
                PlayerControlHelper.PlayerControlAction.TogglePlayPause => 0.3,
                _ => 0.5
            };

            if (_random.NextDouble() < chance)
            {
                disabled.Add(action);
            }
        }

        if (disabled.Count >= PlayerControlHelper.DefaultActionWeights.Count)
        {
            var actions = PlayerControlHelper.DefaultActionWeights.Keys.ToList();
            disabled.Remove(actions[_random.Next(actions.Count)]);
        }

        return disabled;
    }

    private PlayerControlHelper.PlayerControlAction? ChooseNextAction(
        IDictionary<PlayerControlHelper.PlayerControlAction, int> counts,
        ISet<PlayerControlHelper.PlayerControlAction> failed,
        ISet<PlayerControlHelper.PlayerControlAction> disabled)
    {
        var candidates = new List<(PlayerControlHelper.PlayerControlAction Action, double Weight)>();

        foreach (var kvp in PlayerControlHelper.DefaultActionWeights)
        {
            if (disabled.Contains(kvp.Key))
            {
                continue;
            }

            if (failed.Contains(kvp.Key))
            {
                continue;
            }

            if (counts.TryGetValue(kvp.Key, out int count) && count >= 2)
            {
                continue;
            }

            double jitter = 0.75 + (_random.NextDouble() * 0.5);
            candidates.Add((kvp.Key, kvp.Value * jitter));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        double total = candidates.Sum(c => c.Weight);
        double roll = _random.NextDouble() * total;

        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll <= 0)
            {
                return candidate.Action;
            }
        }

        return candidates.Last().Action;
    }
}
