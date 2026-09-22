using loxxking_backend_clean.Domain.Entities.Orders;
using loxxking_backend_clean.Domain.Entities.Invoices;
using loxxking_backend_clean.Domain.Entities.Inventory;

namespace loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<CreateOrderResponse>>
{
    private static readonly SemaphoreSlim _orderNumberLock = new(1, 1);
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IOrderNotificationService _notificationService;
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache _cache;

    public CreateOrderHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IInvoicePdfGenerator pdfGenerator,
        IOrderNotificationService notificationService,
        Microsoft.Extensions.Caching.Distributed.IDistributedCache cache)
    {
        _context = context;
        _currentUserService = currentUserService;
        _pdfGenerator = pdfGenerator;
        _notificationService = notificationService;
        _cache = cache;
    }

    public async Task<Result<CreateOrderResponse>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.Items == null || !request.Items.Any())
            return Result.Failure<CreateOrderResponse>(new Error("Error.Validation", "Order_EmptyItems"));

        var currentUserId = _currentUserService.UserId;

        if (currentUserId != Guid.Empty)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == currentUserId, cancellationToken);
            if (!userExists)
            {
                currentUserId = Guid.Empty;
            }
        }

        // The order country is the country whose prices the customer was shown and chose at
        // checkout — one of the store's countries (per-country pricing, 2026-09-21). It is no longer
        // guessed: checkout used to send a translation key as the country name, so every guest
        // order fell through to the default country (Egypt). Each line is priced at that country's
        // price and the order carries its currency.
        var countryEntity = request.CountryId is Guid requestedCountryId && requestedCountryId != Guid.Empty
            ? await _context.Countries.FirstOrDefaultAsync(c => c.Id == requestedCountryId && c.IsActive && !c.IsDeleted, cancellationToken)
            : null;
        if (countryEntity is null)
            return Result.Failure<CreateOrderResponse>(new Error("Error.Validation", "Order_CountryNotSold"));

        Guid? finalCountryId = countryEntity.Id;
        var resolvedCountryName = countryEntity.Name;

        Order order;
        Invoice invoice;
        var notificationItems = new List<OrderNotificationItem>();

        await _orderNumberLock.WaitAsync(cancellationToken);
        try
        {
            var year = DateTime.UtcNow.Year;
            var orderNumber = await GenerateOrderNumberAsync(year, cancellationToken);

            order = Order.Create(
                currentUserId != Guid.Empty ? currentUserId : null,
                finalCountryId.Value,
                orderNumber,
                request.Address,
                request.Phone,
                request.Notes,
                request.PaymentMethod,
                request.GuestName,
                null, // guestPhone
                null, // guestAddress
                countryEntity.Currency
            );

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            foreach (var itemDto in request.Items)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == itemDto.ProductId, cancellationToken);
                if (product == null) continue;

                var countryPrice = await CountryPriceAsync(product.Id, finalCountryId.Value, cancellationToken);
                if (countryPrice is not decimal price)
                    return Result.Failure<CreateOrderResponse>(new Error("Error.Validation", "Order_ProductNotSoldInCountry"));
                
                var inventoryResult = await DecrementInventoryAsync(product.Id, finalCountryId.Value, itemDto.Quantity, product.NameEn, cancellationToken);
                if (inventoryResult.IsFailure) return Result.Failure<CreateOrderResponse>(inventoryResult.Error);

                order.AddItem(product.Id, itemDto.Quantity, loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(price));

                notificationItems.Add(new OrderNotificationItem(product.NameEn, itemDto.Quantity, price));
            }

            invoice = Invoice.Create(
                order.Id,
                $"INV-{order.OrderNumber}",
                order.TotalAmount
            );

            _context.Orders.Add(order);
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _orderNumberLock.Release();
        }

        foreach (var itemDto in request.Items)
        {
            await _cache.RemoveAsync($"ProductDetail_{itemDto.ProductId}_ar", cancellationToken);
            await _cache.RemoveAsync($"ProductDetail_{itemDto.ProductId}_en", cancellationToken);
        }

        await ProcessInvoicesAndNotificationsAsync(order, invoice, request, currentUserId, resolvedCountryName, notificationItems, cancellationToken);

        return Result.Success(new CreateOrderResponse(order.Id, order.OrderNumber, order.TotalAmount.Value, finalCountryId.Value, order.Currency));
    }

    /// <summary>
    /// The product's price in the order country, or null when it has none there. There is no
    /// fallback: the storefront never offers a product without the visitor's price, and the old
    /// fallback (any other country's price, then the EGP base price) charged the wrong amount.
    /// </summary>
    private Task<decimal?> CountryPriceAsync(Guid productId, Guid countryId, CancellationToken cancellationToken) =>
        _context.ProductPrices
            .Where(p => p.ProductId == productId && p.CountryId == countryId)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Result> DecrementInventoryAsync(Guid productId, Guid countryId, int quantity, string productName, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 3; i++)
        {
            InventoryItem? inventory = null;
            var savepointName = $"inv_{i}_{productId:N}"[..30];
            try
            {
                var currentTransaction = _context.Database.CurrentTransaction;
                if (currentTransaction != null)
                {
                    await currentTransaction.CreateSavepointAsync(savepointName, cancellationToken);
                }

                inventory = await _context.InventoryItems
                    .FirstOrDefaultAsync(inv => inv.ProductId == productId && inv.CountryId == countryId, cancellationToken);
                
                inventory ??= await _context.InventoryItems
                    .FirstOrDefaultAsync(inv => inv.ProductId == productId, cancellationToken);

                if (inventory == null || inventory.Quantity < quantity)
                    return Result.Failure(new Error("Error.Validation", $"Insufficient inventory for product {productName}"));

                inventory.RemoveStock(quantity);
                _context.InventoryItems.Update(inventory);
                
                await loxxking_backend_clean.Application.Features.Inventories.Helpers.InventorySyncHelper.SyncProductStockAsync(productId, _context, cancellationToken);
                
                await _context.SaveChangesAsync(cancellationToken);
                
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                var currentTransaction = _context.Database.CurrentTransaction;
                
                if (currentTransaction != null)
                {
                    await currentTransaction.RollbackToSavepointAsync(savepointName, cancellationToken);
                }

                if (i == 2)
                {
                    return Result.Failure(new Error("Error.Concurrency", "Order_ConcurrencyRetry"));
                }
                
                if (inventory != null)
                {
                    _context.Entry(inventory).State = EntityState.Detached;
                }
            }
        }
        
        return Result.Failure(new Error("Error.Concurrency", "Order_ConcurrencyRetry"));
    }

    private async Task ProcessInvoicesAndNotificationsAsync(Order order, Invoice invoice, CreateOrderCommand request, Guid currentUserId, string? resolvedCountryName, List<OrderNotificationItem> notificationItems, CancellationToken cancellationToken)
    {
        byte[]? pdfAttachment = null;
        try
        {
            pdfAttachment = await _pdfGenerator.GeneratePdfAsync(invoice, cancellationToken);
        }
        catch (Exception)
        {
        }

        var customerUser = currentUserId != Guid.Empty 
            ? await _context.Users.FindAsync(new object?[] { currentUserId }, cancellationToken: cancellationToken)
            : null;
        var customerName = customerUser?.Name ?? request.GuestName ?? "Customer";
        var customerLang = customerUser?.PreferredLanguage ?? (System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en");

        var notifData = new OrderNotificationData(
            order.OrderNumber,
            customerName,
            order.Phone ?? "",
            order.Address ?? "",
            resolvedCountryName ?? "",
            order.PaymentMethod.ToString(),
            order.TotalAmount.Value,
            notificationItems,
            order.CreatedAt,
            pdfAttachment,
            customerLang
        );

        _ = _notificationService.NotifyNewOrderAsync(notifData, CancellationToken.None)
            .ContinueWith(t => 
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Console.WriteLine($"[CRITICAL] Order notification background task failed for order {order.OrderNumber}: {t.Exception.Flatten().Message}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task<string> GenerateOrderNumberAsync(int year, CancellationToken cancellationToken)
    {
        var prefix = $"ORD-{year}-";

        var candidates = await _context.Orders
            .IgnoreQueryFilters()
            .Where(o => o.OrderNumber != null && o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber.Length)
            .ThenByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .Take(50)
            .ToListAsync(cancellationToken);

        int maxSequence = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Length > prefix.Length)
            {
                var suffix = candidate.Substring(prefix.Length);
                if (int.TryParse(suffix, out var seq) && seq > maxSequence)
                {
                    maxSequence = seq;
                }
            }
        }

        var nextSequence = maxSequence + 1;
        var orderNumber = $"{prefix}{nextSequence:D4}";

        while (await _context.Orders.IgnoreQueryFilters().AnyAsync(o => o.OrderNumber == orderNumber, cancellationToken))
        {
            nextSequence++;
            orderNumber = $"{prefix}{nextSequence:D4}";
        }

        return orderNumber;
    }
}
