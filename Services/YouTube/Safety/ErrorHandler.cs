using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace GPM_driver.Services.YouTube.Safety;

internal static class ErrorHandler
{
    internal static async Task<bool> RunSafeAsync(Func<Task> action, ILogger logger, string scope)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTube warmup step '{Scope}' failed.", scope);
            return false;
        }
    }
}
