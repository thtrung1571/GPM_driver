using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services.YouTube.Core;

internal sealed class SessionManager
{
    private readonly IBrowserContext _context;
    private readonly ILogger _logger;

    internal SessionManager(IBrowserContext context, ILogger logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async Task<IPage> EnsurePageAsync(CancellationToken cancellationToken)
    {
        var page = _context.Pages.FirstOrDefault(p => !p.IsClosed);
        if (page != null)
        {
            return page;
        }

        _logger.LogDebug("No available page, opening dedicated YouTube warmup tab.");
        page = await _context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        return page;
    }
}
