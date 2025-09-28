using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GPM_driver.Helpers;
using GPM_driver.Models;
using GPM_driver.Services.YouTube;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GPM_driver.Services;

internal class YouTubeWarmupService
{
    private readonly IBrowserContext _context;
    private readonly ILogger<YouTubeWarmupService>? _logger;
    private readonly Random _random = RandomProvider.Shared;

    private IPage? _page;
    private MouseHelper? _mouseHelper;
    private KeyboardHelper? _keyboardHelper;


    public YouTubeWarmupService(IBrowserContext context, ILogger<YouTubeWarmupService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }
}
