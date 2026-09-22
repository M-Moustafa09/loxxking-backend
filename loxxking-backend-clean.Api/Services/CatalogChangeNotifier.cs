using loxxking_backend_clean.Api.Hubs;
using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace loxxking_backend_clean.Api.Services;

public class CatalogChangeNotifier : ICatalogChangeNotifier
{
    private readonly IHubContext<CatalogHub> _hubContext;
    private readonly ILogger<CatalogChangeNotifier> _logger;

    public CatalogChangeNotifier(IHubContext<CatalogHub> hubContext, ILogger<CatalogChangeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task CatalogChangedAsync(IReadOnlyCollection<Guid>? productIds)
    {
        // The change is already saved: a failed broadcast only means open pages refresh later, so it
        // must never fail the admin's request.
        try
        {
            await _hubContext.Clients.All.SendAsync("CatalogChanged", new { productIds });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not broadcast a catalogue change to the storefronts.");
        }
    }
}
