namespace loxxking_backend_clean.Application.Features.Support.Queries.GetMessages;

public class GetMessagesHandler : IRequestHandler<GetMessagesQuery, Result<List<GetMessagesResponse>>>
{
    private readonly IApplicationDbContext _context;

    public GetMessagesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<GetMessagesResponse>>> Handle(GetMessagesQuery request, CancellationToken cancellationToken)
    {
        // Not the caller's conversation: answer as if it did not exist, without saying whose it is.
        if (!await SupportConversationAccess.CanReadAsync(_context, request.ConversationId, request.UserId, request.GuestId, request.IsStaff, cancellationToken))
            return Result.Failure<List<GetMessagesResponse>>(new Error("Error.NotFound", "Conversation not found"));

        var messages = await _context.SupportMessages
            .Where(sm => sm.ConversationId == request.ConversationId)
            .OrderBy(sm => sm.CreatedAt)
            .Select(sm => new GetMessagesResponse(
                sm.Id,
                sm.ConversationId,
                sm.Message,
                sm.CreatedAt,
                sm.IsRead,
                sm.IsStaff ? "Staff" : (sm.SenderId != null ? "Customer" : "Guest"),
                sm.IsStaff ? (sm.GuestName ?? "Support") : (sm.Sender != null ? sm.Sender.Name : (sm.GuestName ?? "Customer")),
                sm.AttachmentUrl
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(messages);
    }
}
