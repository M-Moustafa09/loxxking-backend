namespace loxxking_backend_clean.Application.Features.Support.Queries.GetMessages;

/// <summary>The caller is checked against the conversation (see SupportConversationAccess).</summary>
public record GetMessagesQuery(Guid ConversationId, Guid? UserId, string? GuestId, bool IsStaff) : IRequest<Result<List<GetMessagesResponse>>>;

public record GetMessagesResponse(
    Guid Id,
    Guid ConversationId,
    string Message,
    DateTime CreatedAt,
    bool IsRead,
    string SenderType,
    string SenderName,
    string? AttachmentUrl = null
);
