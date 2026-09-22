using loxxking_backend_clean.Application.Features.Support.Commands.MarkConversationRead;
using loxxking_backend_clean.Application.Features.Support.Commands.SendMessage;
using loxxking_backend_clean.Application.Features.Support.Queries.GetConversations;
using loxxking_backend_clean.Application.Features.Support.Queries.GetMessages;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/support-chat")]
[Route("api/chat")]
public class SupportChatController : ControllerBase
{
    private readonly ISender _sender;
    public SupportChatController(ISender sender) { _sender = sender; }

    [HttpGet("messages/{conversationId}")]
    [HttpGet("conversations/{conversationId:guid}/messages")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMessages(Guid conversationId, [FromHeader(Name = "X-Guest-Id")] string? guestId, CancellationToken ct)
        => (await _sender.Send(ReadQuery(conversationId, guestId), ct)).ToApiResponse();

    [HttpGet("conversations/{conversationId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetConversationById(Guid conversationId, [FromHeader(Name = "X-Guest-Id")] string? guestId, CancellationToken ct)
        => (await _sender.Send(ReadQuery(conversationId, guestId), ct)).ToApiResponse();

    private GetMessagesQuery ReadQuery(Guid conversationId, string? guestId)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var userId = userIdStr is not null && Guid.TryParse(userIdStr, out var id) ? (Guid?)id : null;
        var isStaff = User.IsInRole("Admin") || User.IsInRole("StoreManager") || User.IsInRole("SalesEmployee");
        return new GetMessagesQuery(conversationId, userId, guestId, isStaff);
    }

    [HttpPost("send")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("VisitorChatLimiter")]
    public async Task<IActionResult> SendMessage(
        [FromBody] ChatMessageInputDto input,
        [FromHeader(Name = "X-Guest-Id")] string? guestId,
        CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var userId = userIdStr is not null && Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
        var text = input.Text ?? input.Message ?? "";
        return (await _sender.Send(new SendMessageCommand(Guid.Empty, text, userId, input.GuestName ?? input.Sender, false, guestId, input.ClientMessageId, input.AttachmentUrl), ct)).ToApiResponse();
    }

    [HttpPost("conversations/{conversationId:guid}/messages")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("VisitorChatLimiter")]
    public async Task<IActionResult> SendConversationMessage(
        Guid conversationId,
        [FromBody] ChatMessageInputDto input,
        [FromHeader(Name = "X-Guest-Id")] string? guestId,
        CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var userId = userIdStr is not null && Guid.TryParse(userIdStr, out var id) ? id : Guid.Empty;
        var text = input.Text ?? input.Message ?? "";
        return (await _sender.Send(new SendMessageCommand(conversationId, text, userId, input.GuestName ?? input.Sender, false, guestId, input.ClientMessageId, input.AttachmentUrl), ct)).ToApiResponse();
    }

    [HttpPost("upload")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadMedia(
        IFormFile file,
        [FromServices] loxxking_backend_clean.Application.Common.Interfaces.IFileStorageService fileStorage,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(loxxking_backend_clean.Shared.ApiResponse<object>.Fail("File is required"));

        var isAudio = (!string.IsNullOrEmpty(file.ContentType) && file.ContentType.StartsWith("audio", StringComparison.OrdinalIgnoreCase)) ||
                      file.FileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                      file.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                      file.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                      file.FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                      file.FileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                      file.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

        var folder = isAudio ? "chat/audio" : "chat/images";
        using var stream = file.OpenReadStream();
        var url = await fileStorage.UploadAsync(stream, file.FileName, file.ContentType, folder, ct);
        return Ok(loxxking_backend_clean.Shared.ApiResponse<object>.Ok(new { url }));
    }

    [HttpGet("conversations")]
    [Authorize(Roles = "Admin,StoreManager,SalesEmployee")]
    public async Task<IActionResult> GetConversations(CancellationToken ct) => (await _sender.Send(new GetConversationsQuery(), ct)).ToApiResponse();

    [HttpPatch("conversations/{conversationId}/read")]
    [HttpPost("conversations/{conversationId}/read")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkRead(
        Guid conversationId,
        [FromHeader(Name = "X-Guest-Id")] string? guestId,
        CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var userId = userIdStr is not null && Guid.TryParse(userIdStr, out var id) ? (Guid?)id : null;
        var isStaff = User.IsInRole("Admin") || User.IsInRole("StoreManager") || User.IsInRole("SalesEmployee");
        return (await _sender.Send(new MarkConversationReadCommand(conversationId, userId, guestId, isStaff), ct)).ToApiResponse();
    }

    [HttpGet("conversations/my")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMyConversation(
        [FromHeader(Name = "X-Guest-Id")] string? guestId,
        CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        var userId = userIdStr is not null && Guid.TryParse(userIdStr, out var id) ? (Guid?)id : null;

        if (!userId.HasValue && string.IsNullOrWhiteSpace(guestId))
        {
            return Ok(new loxxking_backend_clean.Shared.ApiResponse<loxxking_backend_clean.Application.Features.Support.Queries.GetMyConversation.ConversationWithMessagesResponse?>
            {
                Success = true,
                Data = null
            });
        }

        return (await _sender.Send(new loxxking_backend_clean.Application.Features.Support.Queries.GetMyConversation.GetMyConversationQuery(userId, guestId), ct)).ToApiResponse();
    }

    [HttpPost("incoming-from-crm")]
    [AllowAnonymous]
    public async Task<IActionResult> IncomingFromCrm([FromBody] IncomingCrmMessageDto dto, [FromHeader(Name = "X-CRM-Key")] string crmKey, [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var expectedKey = configuration["LegacyCrm:IncomingKey"];
        if (string.IsNullOrEmpty(expectedKey) || crmKey != expectedKey)
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(dto.VisitorSessionId, out var conversationId))
            return BadRequest("Invalid VisitorSessionId format.");

        var text = dto.Message ?? "";
        // Sending as Staff so it shows properly on Frontend (isStaff = true). We leave UserId empty since CRM employees don't map to Loxxking Users.
        // We set GuestName to EmployeeName so frontend can display "EmployeeName" for the reply.
        var cmd = new SendMessageCommand(conversationId, text, Guid.Empty, dto.EmployeeName, true, null, dto.ClientMessageId, dto.AttachmentUrl, IsFromCrm: true);

        return (await _sender.Send(cmd)).ToApiResponse();
    }
}

public record ChatMessageInputDto(string? Text, string? Message, string? Sender, string? ClientMessageId, string? AttachmentUrl, string? GuestName = null);
public record IncomingCrmMessageDto(string VisitorSessionId, string ClientMessageId, string StoreName, string? Message, string? AttachmentUrl, string EmployeeName);