using loxxking_backend_clean.Domain.Entities.BankTransfers;
using loxxking_backend_clean.Domain.Entities.Notifications;


namespace loxxking_backend_clean.Application.Features.BankTransfers.Commands.UploadTransfer;

public class UploadTransferHandler : IRequestHandler<UploadTransferCommand, Result<UploadTransferResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IStringLocalizer<SharedResource>? _localizer;

    public UploadTransferHandler(IApplicationDbContext context, IFileStorageService fileStorageService, IStringLocalizer<SharedResource>? localizer = null)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _localizer = localizer;
    }

    // Images only (owner decision 2026-09-30, as on Moon Light): the transfer is reviewed in the CRM,
    // which shows a receipt as an image and takes nothing else — a PDF would never leave this store.
    private static readonly Dictionary<string, string> ReceiptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/pjpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/x-png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public async Task<Result<UploadTransferResponse>> Handle(UploadTransferCommand request, CancellationToken cancellationToken)
    {
        if (!ReceiptExtensions.TryGetValue((request.ContentType ?? "").Trim(), out var extension))
        {
            return Result.Failure<UploadTransferResponse>(new Error("Error.Validation", "BankTransfer_OnlyImagesAllowed"));
        }

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<UploadTransferResponse>(new Error("Error.NotFound", "Order_NotFound"));
        }

        var existing = await _context.BankTransfers
            .FirstOrDefaultAsync(bt => bt.OrderId == request.OrderId, cancellationToken);

        // Stored under the type's own extension, whatever the customer's file was called: the CRM goes by it.
        var imageUrl = await _fileStorageService.UploadAsync(request.FileStream, "receipt" + extension, request.ContentType!, "bank-transfers", cancellationToken);

        BankTransfer transfer;
        if (existing != null)
        {
            _context.BankTransfers.Remove(existing);
            transfer = BankTransfer.Create(request.OrderId, imageUrl);
            _context.BankTransfers.Add(transfer);
        }
        else
        {
            transfer = BankTransfer.Create(request.OrderId, imageUrl);
            _context.BankTransfers.Add(transfer);
        }

        order.SetBankTransferReceiptUrl(imageUrl);
        order.ChangePaymentStatus(PaymentStatus.PendingVerification);
        _context.Orders.Update(order);

        await _context.SaveChangesAsync(cancellationToken);

        var reviewers = await _context.Users
            .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.StoreManager)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var notifMsg = _localizer.Get("Notification_BankTransferSubmitted", "A new bank transfer proof needs review.");
        foreach (var reviewerId in reviewers)
        {
            _context.Notifications.Add(Notification.Create(
                reviewerId,
                NotificationType.BankTransferSubmitted,
                notifMsg,
                order.Id
            ));
        }
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new UploadTransferResponse(transfer.Id, transfer.Status.ToString()));
    }
}
