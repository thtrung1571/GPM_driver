using System;
using System.Threading.Tasks;

using GPM_driver.Services.YouTube.Core;
using Microsoft.Extensions.Logging;

namespace GPM_driver.Services.YouTube.Activities;

internal abstract class BaseActivity
{
    protected BaseActivity(string name, ILogger logger)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected ILogger Logger { get; }

    internal string Name { get; }

    internal async Task<bool> TryExecuteAsync(WarmupContext context)
    {
        try
        {
            var result = await ExecuteAsync(context);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Activity {Activity} failed.", Name);
            return false;
        }
    }

    protected abstract Task<bool> ExecuteAsync(WarmupContext context);
}
