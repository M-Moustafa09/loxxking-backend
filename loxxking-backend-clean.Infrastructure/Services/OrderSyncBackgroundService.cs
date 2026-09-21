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
                await ProcessPendingOrdersAsync(stoppingToken);
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

    private async Task ProcessPendingOrdersAsync(CancellationToken stoppingToken)
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
            .Where(o => !o.IsSynced && !o.IsDeleted)
            .OrderBy(o => o.CreatedAt)
            .Take(20)
            .ToListAsync(stoppingToken);

        if (!pendingOrders.Any())
        {
            return;
        }

        foreach (var order in pendingOrders)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var syncDto = new CrmOrderSyncDto
            {
                LoxxkingOrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerName = order.Customer?.Name ?? order.GuestName ?? "Unknown",
                Phone = order.Phone,
                Address = order.Address,
                City = order.City,
                Country = order.Country.Name,
                Notes = order.Notes,
                PaymentMethod = order.PaymentMethod.ToString(),
                TotalAmount = order.TotalAmount.Value,
                CreatedAt = order.CreatedAt,
                Items = order.OrderItems.Select(i => new CrmOrderItemSyncDto
                {
                    ProductName = i.Product?.NameAr ?? i.Product?.NameEn ?? i.ProductId.ToString(),
                    ProductCode = i.Product?.ProductCode, // Luxira/CRM code for CRM-side resolution (G3.1)
                    Quantity = i.Quantity,
                    UnitPrice = i.PriceAtOrder
                }).ToList()
            };

            var result = await syncService.SyncOrderAsync(syncDto, stoppingToken);

            if (result.IsSuccess)
            {
                order.MarkAsSynced();
                _logger.LogInformation("Order {OrderId} marked as synced.", order.Id);
            }
            else
            {
                _logger.LogWarning("Failed to sync order {OrderId}. It will be retried in the next cycle. Error: {Error}", order.Id, result.Error?.Message);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }
}
