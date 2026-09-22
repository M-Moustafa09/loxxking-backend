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

        var createHandler = new CreateOrderHandler(db, currentUserMock.Object, pdfMock.Object, notifMock.Object, GetCache());
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

        var createHandler = new CreateOrderHandler(db, currentUserMock.Object, pdfMock.Object, notifMock.Object, GetCache());

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
        var handler = new CreateOrderHandler(db, currentUserMock.Object, new Mock<IInvoicePdfGenerator>().Object, new Mock<IOrderNotificationService>().Object, GetCache());

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
    }
}
