using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.Support;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace loxxking_backend_clean.Api.Hubs;

public class ChatHub : Hub
{
    private readonly IApplicationDbContext _context;

    public ChatHub(IApplicationDbContext context)
    {
        _context = context;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Only the conversation's customer or staff may join its group: the group receives every message.
    /// A guest proves who they are with the `guestId` query value (the storefront's `lk-guest-id`),
    /// the same id the chat endpoints read from X-Guest-Id — a WebSocket cannot carry that header.
    /// </summary>
    public async Task JoinConversation(string conversationId)
    {
        var user = Context.User;
        var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
        Guid? userId = Guid.TryParse(userIdStr, out var id) ? id : null;
        var isStaff = user is not null && (user.IsInRole("Admin") || user.IsInRole("StoreManager") || user.IsInRole("SalesEmployee"));
        var guestId = Context.GetHttpContext()?.Request.Query["guestId"].ToString();

        if (!Guid.TryParse(conversationId, out var conversation)
            || !await SupportConversationAccess.CanReadAsync(_context, conversation, userId, guestId, isStaff, Context.ConnectionAborted))
        {
            throw new HubException("Unauthorized");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversation}");
    }

    public async Task LeaveConversation(string conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
    }
}
