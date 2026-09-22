using loxxking_backend_clean.Domain.Entities.Support;

namespace loxxking_backend_clean.Application.Features.Support;

/// <summary>
/// Who may read a support conversation: staff, or its customer — matched the way sending a message already
/// matches them (SendMessageHandler): a signed-in customer by email or name, a guest by the id the
/// storefront keeps in localStorage (`lk-guest-id`, sent as X-Guest-Id).
///
/// The conversation id alone is not proof: reading the messages and joining the live chat group used to
/// need nothing more, so anyone holding the id could read or listen in.
/// </summary>
public static class SupportConversationAccess
{
    public static async Task<bool> CanReadAsync(
        IApplicationDbContext context, Guid conversationId, Guid? userId, string? guestId, bool isStaff, CancellationToken cancellationToken)
    {
        var conversation = await context.SupportConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null) return false;
        if (isStaff) return true;

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
            if (user is not null && (conversation.CustomerEmail == user.Email || conversation.CustomerName == user.Name))
                return true;
        }

        return IsGuestOwner(conversation, guestId);
    }

    private static bool IsGuestOwner(SupportConversation conversation, string? guestId)
        => !string.IsNullOrWhiteSpace(guestId) && Guid.TryParse(guestId, out _)
           && (conversation.OrderNumber == $"guest:{guestId}" || conversation.CustomerEmail == $"guest_{guestId}@guest.local");
}
