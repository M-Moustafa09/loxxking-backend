using loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder;
using loxxking_backend_clean.Application.Features.Orders.Commands.UpdateOrderStatus;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Domain.Entities.Orders;
using loxxking_backend_clean.Infrastructure.Persistence;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using loxxking_backend_clean.Application.Common.Interfaces;
using Moq;

namespace loxxking_backend_clean.Api.IntegrationTests;

public class VerifyOrdersRuntimeBehavior
{
    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");
        return context;
    }

    private Microsoft.Extensions.Caching.Distributed.IDistributedCache GetCache()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions());
        return new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(opts);
    }

    /// <summary>The CRM's city list for every country; null = the CRM could not be read.</summary>
    private static ICheckoutCityDirectory Cities(params string[]? cities)
    {
        var directory = new Mock<ICheckoutCityDirectory>();
        directory.Setup(d => d.GetCitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cities is { Length: > 0 } ? cities : null);
        return directory.Object;
    }

    [Fact]
    public async Task Verify_Order_Create_And_Status_Update()
    {
        var db = GetDbContext();
        
        var currentUserMock = new Mock<ICurrentUserService>();
        var pdfMock = new Mock<IInvoicePdfGenerator>();
        var notifMock = new Mock<IOrderNotificationService>();
        
        var country = Country.Create("TestCountry", "USD", "en", true);
        db.Countries.Add(country);
        
        var user = loxxking_backend_clean.Domain.Entities.Users.User.Create("Test User", "test@test.com", "123", "hash", country.Id, loxxking_backend_clean.Domain.Enums.UserRole.Customer, "en");
        db.Users.Add(user);
        
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-slug", "");
        db.Categories.Add(category);

        var product = Product.Create(
            category.Id, 
            "Test AR", 
            "Test Product", 
            "Desc EN", 
            "slug-test", 
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(100), 
            null, 
            new List<string>(), 
            new List<string>(), 
            new List<string>(), 
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        // Orders are priced at the order country's price (per-country pricing, 2026-09-21): no fallback.
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = country.Id, Price = 100 });
        await db.SaveChangesAsync();
        
        var inventoryId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4 };
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO InventoryItems (Id, ProductId, CountryId, Quantity, RowVersion, CreatedAt, UpdatedAt, IsActive, IsDeleted) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            inventoryId, product.Id, country.Id, 100, rowVersion, DateTime.UtcNow, DateTime.UtcNow, 1, 0);
        

        
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var createHandler = new CreateOrderHandler(db, currentUserMock.Object, pdfMock.Object, notifMock.Object, GetCache(), Cities());
        var createCmd = new CreateOrderCommand(
            "123 Main St",
            "+1234567890",
            "Please deliver fast",
            PaymentMethod.CashOnDelivery,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> 
            { 
                new loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto(product.Id, 2) 
            },
            country.Id,
            "Guest Name",
            null
        );
        
        var createResult = await createHandler.Handle(createCmd, CancellationToken.None);
        Assert.True(createResult.IsSuccess);

        var createdOrder = await db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == createResult.Value.OrderId);
        Assert.NotNull(createdOrder);
        Assert.Equal($"ORD-{DateTime.UtcNow.Year}-0001", createdOrder.OrderNumber);
        Assert.Equal(200m, createdOrder.TotalAmount.Value);
        Assert.Single(createdOrder.OrderItems);
        Assert.Equal(2, createdOrder.OrderItems.First().Quantity);
        Assert.Equal(100m, createdOrder.OrderItems.First().PriceAtOrder);

        var updateStatusHandler = new UpdateOrderStatusHandler(db);
        var updateCmd = new UpdateOrderStatusCommand(createResult.Value.OrderId, OrderStatus.Prepared, user.Id);
        
        var updateResult = await updateStatusHandler.Handle(updateCmd, CancellationToken.None);
        Assert.True(updateResult.IsSuccess);
        
        var updatedOrder = await db.Orders.FindAsync(createResult.Value.OrderId);
        Assert.Equal(OrderStatus.Prepared, updatedOrder!.Status);
    }

    [Fact]
    public async Task Verify_Sequential_OrderNumber_Generation_And_Legacy_Handling()
    {
        var db = GetDbContext();
        var year = DateTime.UtcNow.Year;

        var currentUserMock = new Mock<ICurrentUserService>();
        var pdfMock = new Mock<IInvoicePdfGenerator>();
        var notifMock = new Mock<IOrderNotificationService>();

        var country = Country.Create("Country", "USD", "en", true);
        db.Countries.Add(country);
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-slug", "");
        db.Categories.Add(category);

        var product = Product.Create(
            category.Id, "AR", "EN", "Desc", "slug-seq",
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(50),
            null, new List<string>(), new List<string>(), new List<string>(),
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        // Orders are priced at the order country's price (per-country pricing, 2026-09-21): no fallback.
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = country.Id, Price = 50 });
        await db.SaveChangesAsync();

        var inventoryId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4 };
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO InventoryItems (Id, ProductId, CountryId, Quantity, RowVersion, CreatedAt, UpdatedAt, IsActive, IsDeleted) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            inventoryId, product.Id, country.Id, 100, rowVersion, DateTime.UtcNow, DateTime.UtcNow, 1, 0);

        // Seed an existing order with ORD-YEAR-0001 and one with the legacy pattern like ORD-20260907-2DDF7D
        var existingOrder1 = Order.Create(null, country.Id, $"ORD-{year}-0001", "Addr", "123", null, PaymentMethod.CashOnDelivery);
        var existingLegacy = Order.Create(null, country.Id, $"ORD-{year}0907-2DDF7D", "Addr", "123", null, PaymentMethod.CashOnDelivery);
        db.Orders.AddRange(existingOrder1, existingLegacy);
        await db.SaveChangesAsync();

        var createHandler = new CreateOrderHandler(db, currentUserMock.Object, pdfMock.Object, notifMock.Object, GetCache(), Cities());

        var cmd1 = new CreateOrderCommand(
            "Addr 1", "+12345", null, PaymentMethod.CashOnDelivery,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 1) },
            country.Id, "Guest", null);

        var res1 = await createHandler.Handle(cmd1, CancellationToken.None);
        Assert.True(res1.IsSuccess);
        Assert.Equal($"ORD-{year}-0002", res1.Value.OrderNumber);

        var cmd2 = new CreateOrderCommand(
            "Addr 2", "+12345", null, PaymentMethod.CashOnDelivery,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 1) },
            country.Id, "Guest", null);

        var res2 = await createHandler.Handle(cmd2, CancellationToken.None);
        Assert.True(res2.IsSuccess);
        Assert.Equal($"ORD-{year}-0003", res2.Value.OrderNumber);
    }

    [Fact]
    public async Task Verify_Order_Uses_Country_Price_And_Currency()
    {
        var db = GetDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        var handler = new CreateOrderHandler(db, currentUserMock.Object, new Mock<IInvoicePdfGenerator>().Object, new Mock<IOrderNotificationService>().Object, GetCache(), Cities());

        var saudi = Country.Create("Saudi Arabia", "SAR", "ar", false);
        var libya = Country.Create("Libya", "LYD", "ar", false);
        db.Countries.AddRange(saudi, libya);
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-price", "");
        db.Categories.Add(category);
        var product = Product.Create(
            category.Id, "AR", "EN", "Desc", "slug-price",
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(2800),
            null, new List<string>(), new List<string>(), new List<string>(),
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = saudi.Id, Price = 350 });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO InventoryItems (Id, ProductId, CountryId, Quantity, RowVersion, CreatedAt, UpdatedAt, IsActive, IsDeleted) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
            Guid.NewGuid(), product.Id, saudi.Id, 100, new byte[] { 1, 2, 3, 4 }, DateTime.UtcNow, DateTime.UtcNow, 1, 0);

        CreateOrderCommand Order(Guid? countryId) => new(
            "Addr", "+966", null, PaymentMethod.CashOnDelivery,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 2) },
            countryId, "Guest", null);

        // Saudi: the Saudi price (not the 2800 base price), in riyals.
        var saudiOrder = await handler.Handle(Order(saudi.Id), CancellationToken.None);
        Assert.True(saudiOrder.IsSuccess);
        Assert.Equal(700m, saudiOrder.Value.TotalAmount);
        Assert.Equal("SAR", saudiOrder.Value.Currency);
        Assert.Equal("SAR", (await db.Orders.FirstAsync(o => o.Id == saudiOrder.Value.OrderId)).Currency);

        // Libya has no price for it: refused, never priced from another country or the base price.
        var libyaOrder = await handler.Handle(Order(libya.Id), CancellationToken.None);
        Assert.True(libyaOrder.IsFailure);
        Assert.Equal("Order_ProductNotSoldInCountry", libyaOrder.Error.Message);

        // No country (a visitor outside the store's countries): refused.
        var noCountry = await handler.Handle(Order(null), CancellationToken.None);
        Assert.True(noCountry.IsFailure);
        Assert.Equal("Order_CountryNotSold", noCountry.Error.Message);

        // City and area typed at checkout are kept on the order, trimmed (free text, 2026-09-22).
        var typed = await handler.Handle(
            Order(saudi.Id) with { City = "  الرياض ", Area = " حي النخيل " },
            CancellationToken.None);
        Assert.True(typed.IsSuccess);
        var typedOrder = await db.Orders.FirstAsync(o => o.Id == typed.Value.OrderId);
        Assert.Equal("الرياض", typedOrder.City);
        Assert.Equal("حي النخيل", typedOrder.Area);
    }

    [Fact]
    public async Task Verify_Order_Succeeds_Without_Inventory()
    {
        var db = GetDbContext();
        var handler = new CreateOrderHandler(db, new Mock<ICurrentUserService>().Object, new Mock<IInvoicePdfGenerator>().Object, new Mock<IOrderNotificationService>().Object, GetCache(), Cities());

        // A product added from the dashboard: a price in Turkey, no inventory rows at all.
        var turkey = Country.Create("Turkey", "TRY", "tr", false);
        db.Countries.Add(turkey);
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-no-stock", "");
        db.Categories.Add(category);
        var product = Product.Create(
            category.Id, "AR", "EN", "Desc", "slug-no-stock",
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(500),
            null, new List<string>(), new List<string>(), new List<string>(),
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = turkey.Id, Price = 900 });
        await db.SaveChangesAsync();

        var result = await handler.Handle(new CreateOrderCommand(
            "Addr", "+90", null, PaymentMethod.CashOnDelivery,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 3) },
            turkey.Id, "Guest", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2700m, result.Value.TotalAmount);
        Assert.Equal("TRY", result.Value.Currency);
    }

    [Fact]
    public async Task Verify_Order_Takes_The_Crm_Spelling_Of_A_Typed_City()
    {
        var db = GetDbContext();
        var egypt = Country.Create("Egypt", "EGP", "ar", false);
        db.Countries.Add(egypt);
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-city", "");
        db.Categories.Add(category);
        var product = Product.Create(
            category.Id, "AR", "EN", "Desc", "slug-city",
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(100),
            null, new List<string>(), new List<string>(), new List<string>(),
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = egypt.Id, Price = 100 });
        await db.SaveChangesAsync();

        async Task<string> OrderCity(string typed, ICheckoutCityDirectory cities)
        {
            var handler = new CreateOrderHandler(db, new Mock<ICurrentUserService>().Object, new Mock<IInvoicePdfGenerator>().Object, new Mock<IOrderNotificationService>().Object, GetCache(), cities);
            var result = await handler.Handle(new CreateOrderCommand(
                "Addr", "+20", null, PaymentMethod.CashOnDelivery,
                new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 1) },
                egypt.Id, "Guest", null) with { City = typed }, CancellationToken.None);
            Assert.True(result.IsSuccess);
            return (await db.Orders.FirstAsync(o => o.Id == result.Value.OrderId)).City;
        }

        var crm = Cities("القاهرة", "الإسكندرية", "الجيزة");
        Assert.Equal("الإسكندرية", await OrderCity("اسكندريه", crm));
        Assert.Equal("الإسكندرية", await OrderCity(" الاسكندرية ", crm));
        Assert.Equal("الجيزة", await OrderCity("جيزه", crm));
        // Not one of the CRM's cities: kept as typed, the order still goes through.
        Assert.Equal("المنصورة", await OrderCity("المنصورة", crm));
        // The CRM could not be read: kept as typed.
        Assert.Equal("اسكندريه", await OrderCity("اسكندريه", Cities()));
    }

    [Fact]
    public async Task Verify_Order_Payment_Method_Is_Cash_Or_Bank_Transfer()
    {
        var db = GetDbContext();
        var egypt = Country.Create("Egypt", "EGP", "ar", false);
        db.Countries.Add(egypt);
        var category = loxxking_backend_clean.Domain.Entities.Categories.Category.Create("Cat AR", "Cat EN", "cat-pay", "");
        db.Categories.Add(category);
        var product = Product.Create(
            category.Id, "AR", "EN", "Desc", "slug-pay",
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(100),
            null, new List<string>(), new List<string>(), new List<string>(),
            "", null, null, null, true, true, null);
        db.Products.Add(product);
        db.ProductPrices.Add(new loxxking_backend_clean.Domain.Entities.Products.ProductPrice { ProductId = product.Id, CountryId = egypt.Id, Price = 100 });
        await db.SaveChangesAsync();

        var handler = new CreateOrderHandler(db, new Mock<ICurrentUserService>().Object, new Mock<IInvoicePdfGenerator>().Object, new Mock<IOrderNotificationService>().Object, GetCache(), Cities());
        Task<loxxking_backend_clean.Shared.Result<CreateOrderResponse>> Place(PaymentMethod method) => handler.Handle(new CreateOrderCommand(
            "Addr", "+20", null, method,
            new List<loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder.OrderItemDto> { new(product.Id, 1) },
            egypt.Id, "Guest", null), CancellationToken.None);
        async Task<Order> Saved(Task<loxxking_backend_clean.Shared.Result<CreateOrderResponse>> placing)
        {
            var result = await placing;
            Assert.True(result.IsSuccess);
            return await db.Orders.FirstAsync(o => o.Id == result.Value.OrderId);
        }

        Assert.Equal(PaymentMethod.CashOnDelivery, (await Saved(Place(PaymentMethod.CashOnDelivery))).PaymentMethod);
        var bank = await Saved(Place(PaymentMethod.BankTransfer));
        Assert.Equal(PaymentMethod.BankTransfer, bank.PaymentMethod);
        Assert.Equal(PaymentStatus.PendingVerification, bank.PaymentStatus);

        // A storefront cached before the fix sends 1 (DebitCard) for cash on delivery.
        var legacyCash = await Saved(Place(PaymentMethod.DebitCard));
        Assert.Equal(PaymentMethod.CashOnDelivery, legacyCash.PaymentMethod);
        Assert.Equal(PaymentStatus.Pending, legacyCash.PaymentStatus);

        // No card or wallet payment is processed: refused, never recorded as paid.
        foreach (var method in new[] { PaymentMethod.CreditCard, PaymentMethod.ApplePay, PaymentMethod.GooglePay, PaymentMethod.PayPal, PaymentMethod.Crypto })
        {
            var refused = await Place(method);
            Assert.True(refused.IsFailure);
            Assert.Equal("Order_PaymentMethodNotSupported", refused.Error.Message);
        }
    }
}
