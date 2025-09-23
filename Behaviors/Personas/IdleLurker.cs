using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors.Personas
{
    internal class IdleLurker : IPersona
    {
        private readonly Random _rng = new Random(Guid.NewGuid().GetHashCode());

        public async Task PerformAsync(
            IPage page,
            MouseHelper mouse,
            KeyboardHelper keyboard,
            RetentionBucket bucket,
            int durationSeconds)
        {
            BehaviorLogger.Log($"IdleLurker starting for {durationSeconds}s (bucket={bucket})");

            var end = DateTime.UtcNow.AddSeconds(durationSeconds <= 0 ? 5 : durationSeconds);
            if (durationSeconds <= 0) await Task.Delay(500);

            // Check for content to occasionally show minimal interest
            bool hasContent = await BehaviorBase.HasSufficientContentAsync(page);
            int activityCount = 0; // Track minimal activities
            
            while (DateTime.UtcNow < end)
            {
                try
                {
                    double choice = _rng.NextDouble();

                    if (choice < 0.15) // 15% chance of any activity
                    {
                        activityCount++;
                        double activityType = _rng.NextDouble();

                        if (activityType < 0.6) // Most common: minimal mouse movement
                        {
                            await mouse.MoveRandomlyAsync(_rng.Next(2, 6));
                            BehaviorLogger.LogAction("IdleMove", "minimal-drift");
                        }
                        else if (activityType < 0.85) // Occasional: subtle hovering
                        {
                            if (hasContent && _rng.NextDouble() < 0.7)
                            {
                                // Very brief, tentative hovering - lurker style
                                bool hovered = await LinkHelper.HoverRandomInternalLinkAsync(page, mouse);
                                if (hovered)
                                {
                                    BehaviorLogger.LogAction("TentativeHover", "curious-peek");
                                    // Very short hover time - lurker doesn't want to seem too interested
                                    await Task.Delay(_rng.Next(800, 2000));
                                }
                            }
                            else
                            {
                                // Fallback: tiny movement
                                await mouse.MoveRandomlyAsync(_rng.Next(1, 4));
                                BehaviorLogger.LogAction("IdleMove", "micro-movement");
                            }
                        }
                        else // Rare: very minimal scrolling
                        {
                            if (_rng.NextDouble() < 0.5) // Only half the time
                            {
                                // Single, small scroll - like accidentally touching mouse wheel
                                await ScrollHelper.ScrollRandomAsync(page, 1);
                                BehaviorLogger.LogAction("AccidentalScroll", "single-small");
                            }
                            else
                            {
                                // Alternative: brief content glance
                                if (hasContent)
                                {
                                    await BehaviorBase.PerformContentInspectionAsync(page, mouse);
                                    BehaviorLogger.LogAction("BriefGlance", "minimal-inspection");
                                }
                            }
                        }
                    }
                    else if (choice < 0.18) // 3% additional chance: "accidental" activity
                    {
                        activityCount++; // Count accidental activities too
                        // Simulate accidental interactions - key characteristic of lurkers
                        double accident = _rng.NextDouble();
                        
                        if (accident < 0.7)
                        {
                            // Accidental mouse drift
                            await Task.Delay(_rng.Next(200, 800));
                            await mouse.MoveRandomlyAsync(_rng.Next(1, 3));
                            BehaviorLogger.LogAction("AccidentalMove", "unintended-drift");
                        }
                        else
                        {
                            // Very rare: accidental single key press (like space or arrow)
                            string[] accidentalKeys = { "Space", "ArrowDown", "ArrowUp" };
                            string key = accidentalKeys[_rng.Next(accidentalKeys.Length)];
                            await page.Keyboard.PressAsync(key);
                            BehaviorLogger.LogAction("AccidentalKeyPress", key);
                            
                            // Brief pause as if realizing the mistake
                            await Task.Delay(_rng.Next(1000, 3000));
                        }
                    }
                    // Else: 82% of the time, complete inactivity

                    // Characteristic long idle periods with high variance
                    int basePause = 6000; // 6 seconds base
                    int variance = 4000;   // ±4 seconds variance
                    
                    // Occasionally much longer pauses (simulating being away from computer)
                    if (_rng.NextDouble() < 0.1) // 10% chance
                    {
                        basePause = 12000; // 12 seconds
                        variance = 8000;   // ±8 seconds
                        BehaviorLogger.LogAction("ExtendedIdle", "away-from-screen");
                    }
                    
                    int pauseMs = TimingDistributions.NormalMs(_rng, basePause, variance, 3000, 20000);
                    await Task.Delay(pauseMs);
                }
                catch (Exception ex)
                {
                    BehaviorLogger.LogAction("Error", ex.Message);
                    await Task.Delay(1000); // Longer error recovery - lurker doesn't rush
                }
            }

            BehaviorLogger.Log($"IdleLurker finished with {activityCount} minimal activities");
        }
    }
}
