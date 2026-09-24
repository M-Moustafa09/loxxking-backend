using loxxking_backend_clean.Application.Common.Interfaces;
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

    /// <summary>The wait before the next try after the <paramref name="attempt"/>-th failure.</summary>
    public static TimeSpan RetryDelay(int attempt)
    {
        var doublings = Math.Clamp(attempt - 1, 0, 20);
        var delay = TimeSpan.FromTicks(FirstRetryDelay.Ticks << doublings);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

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
        CreatedAt = order.CreatedAt,
        Items = order.OrderItems.Select(i => new CrmOrderItemSyncDto
        {
            ProductName = i.Product?.NameAr ?? i.Product?.NameEn ?? i.ProductId.ToString(),
            ProductCode = i.Product?.ProductCode, // Luxira/CRM code for CRM-side resolution (G3.1)
            Quantity = i.Quantity,
            UnitPrice = i.PriceAtOrder
        }).ToList()
    };
}
