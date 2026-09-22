using Microsoft.AspNetCore.SignalR;

namespace loxxking_backend_clean.Api.Hubs;

/// <summary>
/// Live storefront updates: every visitor, signed in or not, listens here for "CatalogChanged".
/// It only sends; there is nothing for a client to call, and nothing private goes out.
/// </summary>
public class CatalogHub : Hub
{
}
