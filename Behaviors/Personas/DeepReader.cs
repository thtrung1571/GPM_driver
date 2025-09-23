using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Personas
{
    internal class DeepReader : IPersona
    {
        private readonly Random _rng = new Random(Guid.NewGuid().GetHashCode());

        public async Task PerformAsync(
            IPage page,
            MouseHelper mouse,
            KeyboardHelper keyboard,
            RetentionBucket bucket,
            int durationSeconds)
        {
            BehaviorLogger.Log($"DeepReader starting for {durationSeconds}s (bucket={bucket})");

            var end = DateTime.UtcNow.AddSeconds(durationSeconds <= 0 ? 5 : durationSeconds);
            if (durationSeconds <= 0) await Task.Delay(500);

            // Check content availability for better behavior adaptation
            bool hasContent = await BehaviorBase.HasSufficientContentAsync(page);
            
            while (DateTime.UtcNow < end)
            {
                try
                {
                    double choice = _rng.NextDouble();

                    if (choice < 0.45)
                    {
                        // Methodical content reading behavior
                        if (hasContent)
                        {
                            await BehaviorBase.PerformReadingBehaviorAsync(page, mouse, intensity: 1);
                            BehaviorLogger.LogAction("ReadingBehavior", "methodical-read");
                        }
                        else
                        {
                            // Fallback to basic scrolling
                            await ScrollHelper.ScrollRandomAsync(page, _rng.Next(1, 2));
                            BehaviorLogger.LogAction("ScrollRandom", "basic-read");
                        }
                    }
                    else if (choice < 0.70)
                    {
                        // Deep content inspection - characteristic of deep readers
                        await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                        BehaviorLogger.LogAction("ContentInspection", "deep-analysis");
                    }
                    else if (choice < 0.85)
                    {
                        // Deliberate scrolling with pauses (reading pattern)
                        await ScrollHelper.ScrollWithKeysAsync(page, _rng.Next(1, 2));
                        BehaviorLogger.LogAction("ScrollWithKeys", "deliberate-read");
                        
                        // Additional pause after keyboard scrolling (simulates reading the new content)
                        await Task.Delay(_rng.Next(1000, 2500));
                    }
                    else if (choice < 0.95)
                    {
                        // Thoughtful link hovering - inspecting before deciding to click
                        bool hovered = await LinkHelper.HoverRandomInternalLinkAsync(page, mouse);
                        if (hovered)
                        {
                            BehaviorLogger.LogAction("LinkHover", "contemplative-hover");
                            // Longer consideration time for deep readers
                            await Task.Delay(_rng.Next(2000, 4000));
                        }
                        else
                        {
                            // Fallback: thorough element inspection
                            await mouse.MoveRandomlyAsync(_rng.Next(15, 30));
                            BehaviorLogger.LogAction("ElementInspection", "thorough-scan");
                        }
                    }
                    else
                    {
                        // Selective link clicking - deep readers are more deliberate
                        if (_rng.NextDouble() < 0.7) // 70% chance to actually click when in this branch
                        {
                            var clickedPage = await LinkHelper.SafeClickRandomInternalLinkAsync(page, mouse, maxAttempts: 3);
                            if (clickedPage == null)
                            {
                                BehaviorLogger.LogAction("LinkClick", "no-suitable-link");
                            }
                            else if (!object.ReferenceEquals(clickedPage, page))
                            {
                                // Deep readers spend more time on new tabs
                                BehaviorLogger.LogAction("LinkClick", "opened-new-tab");
                                await Task.Delay(_rng.Next(3000, 8000)); // Longer engagement
                                
                                // Sometimes perform brief reading on the new tab
                                if (_rng.NextDouble() < 0.4)
                                {
                                    await BehaviorBase.PerformReadingBehaviorAsync(clickedPage, mouse, intensity: 1);
                                    await Task.Delay(_rng.Next(2000, 4000));
                                }
                                
                                try { await clickedPage.CloseAsync(); } catch { }
                                try { await page.BringToFrontAsync(); } catch { }
                            }
                            else
                            {
                                BehaviorLogger.LogAction("LinkClick", "same-tab-navigation");
                                // Brief pause to "read" the new content
                                await Task.Delay(_rng.Next(1000, 3000));
                            }
                        }
                        else
                        {
                            // Decided not to click - just inspect more content
                            await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                            BehaviorLogger.LogAction("ContentInspection", "instead-of-click");
                        }
                    }

                    // Occasional micro-movements (but less frequent than fast scanner)
                    if (_rng.NextDouble() < 0.3)
                    {
                        await mouse.MoveRandomlyAsync(_rng.Next(8, 18));
                    }

                    // Longer, more variable reading pauses - deep readers take their time
                    int pauseMs = TimingDistributions.NormalMs(_rng, meanMs: 4000, stdDevMs: 2000, minMs: 1500, maxMs: 12000);
                    await Task.Delay(pauseMs);
                }
                catch (Exception ex)
                {
                    BehaviorLogger.LogAction("Error", ex.Message);
                    await Task.Delay(800); // Longer error recovery - deep readers are patient
                }
            }

            BehaviorLogger.Log("DeepReader finished");
        }
    }
}
