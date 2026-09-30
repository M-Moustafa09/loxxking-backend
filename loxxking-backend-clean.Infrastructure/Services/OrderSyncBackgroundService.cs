using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Services;

public class OrderSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderSyncBackgroundService> _logger;

    public OrderSyncBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OrderSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderSyncBackgroundService is starting.");

        // Give the host time to fully start before doing work
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOrdersAsync(DateTime.UtcNow, stoppingToken);
                // After the orders: a receipt can only be attached to an order the CRM already has.
                await ProcessPendingReceiptsAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down, exit gracefully
                break;
            }
            catch (ObjectDisposedException)
            {
                // ServiceProvider disposed during shutdown, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing pending orders for sync.");
            }

            try
            {
                // Wait for 15 seconds before checking again
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("OrderSyncBackgroundService is stopping.");
    }

    // Waits after a failed send: 30 s, 1, 2, 4, 8, 16, 32 minutes, then every hour.
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);
    // From this many failed attempts an order is logged as an error: someone should look at it.
    private const int StuckAfterAttempts = 5;
    // The sync service's answers when the CRM link itself is off or unconfigured: no order is at fault.
    private static readonly HashSet<string> LinkUnavailableCodes = new() { "SyncDisabled", "ConfigMissing" };

    /// <summary>
    /// One pass: sends the unsent orders that are due, oldest first. An order the CRM refuses waits
    /// longer after each failure and is never picked before its time, so a few orders the CRM keeps
    /// refusing no longer fill the batch and hold back every new order behind them (they used to be
    /// retried first, every 15 seconds, forever). Each outcome is saved as soon as it is known, so an
    /// order the CRM accepted is not sent again because a later one in the batch threw.
    /// </summary>
    public async Task ProcessPendingOrdersAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<ILegacyCrmSyncService>();

        // IgnoreQueryFilters keeps the lines of a product deleted after the order was placed — the
        // Product filter would otherwise drop them and the CRM would get an order missing items.
        // It also lifts the Order filter, so deleted orders are excluded by hand.
        var pendingOrders = await dbContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.Product)
            .Include(o => o.Country)
            .Include(o => o.Customer)
            .Where(o => !o.IsSynced && !o.IsDeleted && (o.NextSyncAttemptAt == null || o.NextSyncAttemptAt <= now))
            .OrderBy(o => o.CreatedAt)
            .Take(20)
            .ToListAsync(stoppingToken);

        foreach (var order in pendingOrders)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var result = await syncService.SyncOrderAsync(ToSyncDto(order), stoppingToken);

            if (result.IsSuccess)
            {
                order.MarkAsSynced();
                _logger.LogInformation("Order {OrderId} marked as synced.", order.Id);
            }
            else if (result.Error is { } error && LinkUnavailableCodes.Contains(error.Code))
            {
                // Nothing will get through until the link is configured; the orders keep their place.
                break;
            }
            else
            {
                var retryAt = now + RetryDelay(order.SyncAttempts + 1);
                order.RecordSyncFailure(result.Error?.Message, retryAt);
                if (order.SyncAttempts >= StuckAfterAttempts)
                    _logger.LogError("Order {OrderNumber} ({OrderId}) has failed to reach the CRM {Attempts} times; next try at {RetryAt:u}. Last error: {Error}",
                        order.OrderNumber, order.Id, order.SyncAttempts, retryAt, result.Error?.Message);
                else
                    _logger.LogWarning("Failed to sync order {OrderId} (attempt {Attempts}); next try at {RetryAt:u}. Error: {Error}",
                        order.Id, order.SyncAttempts, retryAt, result.Error?.Message);
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }

    // A receipt that cannot go as it is (file gone from the disk, or the CRM refuses the file itself)
    // is looked at again once a day rather than on the order backoff: only a person can change that.
    private static readonly TimeSpan UnsendableReceiptDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// One pass: hands the CRM the bank-transfer receipts customers uploaded at checkout, oldest first.
    /// The CRM attaches each to its order where its staff attach one, so whoever reviews the transfer
    /// in the CRM sees the receipt (it used to stay in this store's dashboard only). Only receipts
    /// still awaiting review, and only once their order is in the CRM. A customer who uploads again
    /// gets a new row, which is sent in turn; the CRM keeps the latest.
    /// </summary>
    public async Task ProcessPendingReceiptsAsync(DateTime now, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<ILegacyCrmSyncService>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var pendingReceipts = await dbContext.BankTransfers
            .Where(t => t.CrmReceiptSentAt == null
                        && t.Status == BankTransferStatus.PendingReview
                        && (t.CrmReceiptNextAttemptAt == null || t.CrmReceiptNextAttemptAt <= now)
                        && t.Order.IsSynced)
            .OrderBy(t => t.SubmittedAt)
            .Take(10)
            .ToListAsync(stoppingToken);

        foreach (var transfer in pendingReceipts)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            Result result;
            var unsendable = false;
            await using (var file = await storage.OpenReadAsync(transfer.ProofImageUrl, stoppingToken))
            {
                if (file is null)
                {
                    unsendable = true;
                    result = Result.Failure(new Error("ReceiptMissing", "Receipt file not found in storage."));
                }
                else
                {
                    var fileName = Path.GetFileName(
                        Uri.TryCreate(transfer.ProofImageUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : transfer.ProofImageUrl);
                    result = await syncService.SendBankTransferReceiptAsync(transfer.OrderId, file, fileName, ContentTypeOf(fileName), stoppingToken);
                    unsendable = result.IsFailure && result.Error.Code == "ReceiptRejected";
                }
            }

            if (result.IsSuccess)
            {
                transfer.MarkCrmReceiptSent(now);
                _logger.LogInformation("Bank-transfer receipt of order {OrderId} sent to the CRM.", transfer.OrderId);
            }
            else if (LinkUnavailableCodes.Contains(result.Error.Code) || stoppingToken.IsCancellationRequested)
            {
                // Nothing will get through until the link is configured; the receipts keep their place.
                break;
            }
            else
            {
                var retryAt = now + (unsendable ? UnsendableReceiptDelay : RetryDelay(transfer.CrmReceiptAttempts + 1));
                transfer.RecordCrmReceiptFailure(result.Error.Message, retryAt);
                _logger.LogWarning("Bank-transfer receipt of order {OrderId} did not reach the CRM (attempt {Attempts}, {Code}); next try at {RetryAt:u}. Error: {Error}",
                    transfer.OrderId, transfer.CrmReceiptAttempts, result.Error.Code, retryAt, result.Error.Message);
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "image/jpeg"
    };

    /// <summary>The wait before the next try after the <paramref name="attempt"/>-th failure.</summary>
    public static TimeSpan RetryDelay(int attempt)
    {
        var doublings = Math.Clamp(attempt - 1, 0, 20);
        var delay = TimeSpan.FromTicks(FirstRetryDelay.Ticks << doublings);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    // The CRM keeps every date in Istanbul time (its GetIstanbulTimeWithOffset); Turkey has had no
    // daylight saving since 2016, so the fixed +3 is the fallback where the zone is not installed.
    private static readonly TimeZoneInfo CrmTimeZone = FindCrmTimeZone();

    private static TimeZoneInfo FindCrmTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        }
        return TimeZoneInfo.CreateCustomTimeZone("Istanbul", TimeSpan.FromHours(3), "Istanbul", "Istanbul");
    }

    /// <summary>
    /// The order's time as the CRM records its own orders: Istanbul time. The store saves UTC, and
    /// the CRM stores what it receives as is, so a store order showed three hours early — one placed
    /// after midnight in Istanbul fell on the previous day in the CRM's daily reports and bonuses.
    /// </summary>
    public static DateTime ToCrmTime(DateTime utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), CrmTimeZone),
            DateTimeKind.Unspecified);

    private static CrmOrderSyncDto ToSyncDto(Domain.Entities.Orders.Order order) => new()
    {
        LoxxkingOrderId = order.Id,
        OrderNumber = order.OrderNumber,
        CustomerName = order.Customer?.Name ?? order.GuestName ?? "Unknown",
        Phone = order.Phone,
        // The CRM order has no area field and no courier asks for one, but the driver does:
        // it leads the address (owner decision 2026-09-22), e.g. «حي النخيل - شارع ...».
        Address = string.IsNullOrWhiteSpace(order.Area)
            ? order.Address
            : $"{order.Area.Trim()} - {order.Address}",
        City = order.City,
        Country = order.Country.Name,
        Notes = order.Notes,
        PaymentMethod = order.PaymentMethod.ToString(),
        TotalAmount = order.TotalAmount.Value,
        Currency = order.Currency,
        CreatedAt = ToCrmTime(order.CreatedAt),
        Items = order.OrderItems.Select(i => new CrmOrderItemSyncDto
        {
            ProductName = i.Product?.NameAr ?? i.Product?.NameEn ?? i.ProductId.ToString(),
            ProductCode = i.Product?.ProductCode, // Luxira/CRM code for CRM-side resolution (G3.1)
            Quantity = i.Quantity,
            UnitPrice = i.PriceAtOrder
        }).ToList()
    };
}
