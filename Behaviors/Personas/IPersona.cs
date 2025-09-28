using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using GPM_driver.Behaviors;
using GPM_driver.Helpers;
using System.Threading.Tasks;

namespace GPM_driver.Behaviors.Personas
{
    internal interface IPersona
    {
        /// <summary>
        /// Perform persona-specific behavior for the given duration.
        /// </summary>
        Task PerformAsync(IPage page, MouseHelper mouse, KeyboardHelper keyboard, RetentionBucket bucket, int durationSeconds);
    }
}