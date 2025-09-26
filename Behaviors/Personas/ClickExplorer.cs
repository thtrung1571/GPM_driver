using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Personas
{
    internal class ClickExplorer : IPersona
    {
        private readonly Random _rng = RandomProvider.Shared;

        public async Task PerformAsync(
            IPage page,
            MouseHelper mouse,
            KeyboardHelper keyboard,
            RetentionBucket bucket,
            int durationSeconds)
        {
            BehaviorLogger.Log($"ClickExplorer starting for {durationSeconds}s (bucket={bucket})");

            var end = DateTime.UtcNow.AddSeconds(durationSeconds <= 0 ? 5 : durationSeconds);
            if (durationSeconds <= 0) await Task.Delay(500);

            // Check content for exploration opportunities
            bool hasContent = await BehaviorBase.HasSufficientContentAsync(page);
            
            while (DateTime.UtcNow < end)
            {
                try
                {
                    double choice = _rng.NextDouble();

                    if (choice < 0.50)
                    {
                        // Primary behavior: Active link clicking with improved logic
                        var clickedPage = await LinkHelper.SafeClickRandomInternalLinkAsync(page, mouse, maxAttempts: 3);
                        if (clickedPage == null)
                        {
                            BehaviorLogger.LogAction("LinkClick", "none-found-or-failed");
                            // Fallback: explore existing content more aggressively
                            await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                            await Task.Delay(_rng.Next(800, 1500));
                        }
                        else if (!object.ReferenceEquals(clickedPage, page))
                        {
                            BehaviorLogger.LogAction("LinkClick", "opened-new-tab");
                            
                            // ClickExplorer behavior: explore the new tab more thoroughly
                            await TabHelper.ExploreNewTabAsync(clickedPage, mouse, _rng.Next(2500, 6000));
                            
                            await TabHelper.SafeCloseTabAsync(clickedPage);
                            await TabHelper.SafeBringToFrontAsync(page);
                        }
                        else
                        {
                            BehaviorLogger.LogAction("LinkClick", "navigated-same-tab");
                            // Brief exploration of the new page content
                            await Task.Delay(_rng.Next(1000, 2500));
                        }
                    }
                    else if (choice < 0.70)
                    {
                        // Enhanced link hovering - ClickExplorer likes to investigate before clicking
                        int hoverAttempts = _rng.Next(2, 4); // Try multiple hovers
                        for (int i = 0; i < hoverAttempts && DateTime.UtcNow < end; i++)
                        {
                            bool hovered = await LinkHelper.HoverRandomInternalLinkAsync(page, mouse);
                            if (hovered)
                            {
                                BehaviorLogger.LogAction("LinkHover", $"exploration-{i+1}");
                                await Task.Delay(_rng.Next(800, 1800)); // Consideration time
                                
                                // Sometimes follow through with a click after hovering
                                if (_rng.NextDouble() < 0.3)
                                {
                                    await Task.Delay(_rng.Next(200, 600));
                                    var clickedPage = await LinkHelper.SafeClickRandomInternalLinkAsync(page, mouse, maxAttempts: 1);
                                    if (clickedPage != null)
                                    {
                                        BehaviorLogger.LogAction("FollowupClick", "after-hover");
                                        break; // Exit hover loop to handle the click result
                                    }
                                }
                            }
                        }
                    }
                    else if (choice < 0.85)
                    {
                        // Exploratory scrolling with purpose
                        if (hasContent)
                        {
                            await BehaviorBase.PerformReadingBehaviorAsync(page, mouse, intensity: 2);
                            BehaviorLogger.LogAction("ExploratoryRead", "content-scanning");
                        }
                        else
                        {
                            // More aggressive scrolling to find content
                            await ScrollHelper.ScrollRandomAsync(page, _rng.Next(3, 6));
                            BehaviorLogger.LogAction("ScrollRandom", "seeking-content");
                        }
                    }
                    else if (choice < 0.95)
                    {
                        // Keyboard-driven exploration
                        await ScrollHelper.ScrollWithKeysAsync(page, _rng.Next(2, 4));
                        BehaviorLogger.LogAction("ScrollWithKeys", "keyboard-explore");
                        
                        // ClickExplorer often combines actions
                        await Task.Delay(_rng.Next(500, 1200));
                        await mouse.MoveRandomlyAsync(_rng.Next(10, 25));
                    }
                    else
                    {
                        // Interactive content exploration
                        await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                        BehaviorLogger.LogAction("ContentInspection", "detailed-exploration");
                        
                        // Sometimes this leads to finding clickable elements
                        if (_rng.NextDouble() < 0.4)
                        {
                            await Task.Delay(_rng.Next(1000, 2000));
                            await LinkHelper.HoverRandomInternalLinkAsync(page, mouse);
                        }
                    }

                    // ClickExplorer has frequent mouse movements (active exploration)
                    if (_rng.NextDouble() < 0.8)
                    {
                        await mouse.MoveRandomlyAsync(_rng.Next(8, 20));
                    }

                    // Medium-length pauses - explorer needs time to evaluate but is more active than deep reader
                    int pauseMs = TimingDistributions.NormalMs(_rng, meanMs: 1500, stdDevMs: 800, minMs: 400, maxMs: 5000);
                    await Task.Delay(pauseMs);
                }
                catch (Exception ex)
                {
                    BehaviorLogger.LogAction("Error", ex.Message);
                    await Task.Delay(600); // Quick recovery for active explorer
                }
            }

            BehaviorLogger.Log("ClickExplorer finished");
        }

    }
}
