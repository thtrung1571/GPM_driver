using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors.Personas;
using GPM_driver.Behaviors.Utils;
using GPM_driver.Helpers;

namespace GPM_driver.Behaviors
{
    internal class UserBehavior
    {
        private readonly IPage _page;
        private readonly Random _rng;

        public UserBehavior(IPage page)
        {
            _page = page;
            _rng = RandomProvider.Shared;
        }

        /// <summary>
        /// Main entry — picks persona & retention bucket and runs the persona behavior.
        /// </summary>
        public async Task RunAsync()
        {
            try
            {
                var personaType = PersonaHelper.GetRandomPersona(_rng);
                var bucket = RetentionBucketHelper.GetRandomBucket(_rng);
                int duration = RetentionBucketHelper.PickDurationSeconds(_rng, bucket);

                BehaviorLogger.Log($"Chosen persona={personaType}, bucket={bucket}, duration={duration}s");

                // prepare helpers
                var mouse = new MouseHelper(_page);
                var keyboard = new KeyboardHelper(_page);

                // create persona implementation
                IPersona personaImpl = personaType switch
                {
                    PersonaType.FastScanner => new FastScanner(),
                    PersonaType.DeepReader => new DeepReader(),
                    PersonaType.IdleLurker => new IdleLurker(),
                    PersonaType.ClickExplorer => new ClickExplorer(),
                    _ => new FastScanner()
                };

                await personaImpl.PerformAsync(_page, mouse, keyboard, bucket, duration);
            }
            catch (Exception ex)
            {
                BehaviorLogger.LogAction("RunAsyncError", ex.Message);
            }
        }
    }
}
