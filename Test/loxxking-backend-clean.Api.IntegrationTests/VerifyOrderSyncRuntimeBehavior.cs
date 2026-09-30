using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Domain.Entities.BankTransfers;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Orders;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Domain.ValueObjects;
using loxxking_backend_clean.Infrastructure.Persistence;
using loxxking_backend_clean.Infrastructure.Services;
using loxxking_backend_clean.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

/// <summary>
/// Orders the CRM keeps refusing wait longer after each failure and no longer hold back the new
/// orders placed after them.
/// </summary>
public class VerifyOrderSyncRuntimeBehavior
{
    /// <summary>The CRM: refuses the order numbers in <see cref="Refused"/>, or answers <see cref="LinkError"/> to all.</summary>
    private sealed class FakeCrm : ILegacyCrmSyncService
    {
        public HashSet<string> Refused { get; } = new();
        public Error? LinkError { get; set; }
        public List<string> Received { get; } = new();

        public Task<Result<CrmSyncResponse>> SyncOrderAsync(CrmOrderSyncDto dto, CancellationToken cancellationToken = default)
        {
            Received.Add(dto.OrderNumber);
            if (LinkError is { } linkError) return Task.FromResult(Result.Failure<CrmSyncResponse>(linkError));
            return Task.FromResult(Refused.Contains(dto.OrderNumber)
                ? Result.Failure<CrmSyncResponse>(new Error("ApiError", "Unknown product code"))
                : Result.Success(new CrmSyncResponse { Success = true, LegacyCrmOrderId = 1 }));
        }

        /// <summary>The receipts the CRM was handed: order id, file name, content type.</summary>
        public List<(Guid OrderId, string FileName, string ContentType)> Receipts { get; } = new();
        public Error? ReceiptError { get; set; }

        public Task<Result> SendBankTransferReceiptAsync(Guid loxxkingOrderId, Stream receipt, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            if (LinkError is { } linkError) return Task.FromResult(Result.Failure(linkError));
            Receipts.Add((loxxkingOrderId, fileName, contentType));
            return Task.FromResult(ReceiptError is { } error ? Result.Failure(error) : Result.Success());
        }
    }

    /// <summary>The store's uploads: only the addresses in <see cref="Files"/> exist.</summary>
    private sealed class FakeStorage : IFileStorageService
    {
        public HashSet<string> Files { get; } = new();

        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string fileUrl, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(Files.Contains(fileUrl) ? new MemoryStream(new byte[] { 1, 2, 3 }) : null);
    }

    /// <summary>Accepts every order and hands each payload to <paramref name="onSend"/>.</summary>
    private sealed class CapturingCrm(Action<CrmOrderSyncDto> onSend) : ILegacyCrmSyncService
    {
        public Task<Result<CrmSyncResponse>> SyncOrderAsync(CrmOrderSyncDto dto, CancellationToken cancellationToken = default)
        {
            onSend(dto);
            return Task.FromResult(Result.Success(new CrmSyncResponse { Success = true, LegacyCrmOrderId = 1 }));
        }

        public Task<Result> SendBankTransferReceiptAsync(Guid loxxkingOrderId, Stream receipt, string fileName, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }

    private readonly FakeStorage _storage = new();

    private (OrderSyncBackgroundService Sync, Func<ApplicationDbContext> Db, FakeCrm Crm) Setup(ILegacyCrmSyncService? crmOverride = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var crm = new FakeCrm();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<ILegacyCrmSyncService>(crmOverride ?? crm);
        services.AddSingleton<IFileStorageService>(_storage);
        var provider = services.BuildServiceProvider();

        ApplicationDbContext Db() => provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db = Db();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");

        return (new OrderSyncBackgroundService(provider, NullLogger<OrderSyncBackgroundService>.Instance), Db, crm);
    }

    private static async Task SeedOrdersAsync(ApplicationDbContext db, DateTime start, int count, string prefix)
    {
        var country = await db.Countries.FirstOrDefaultAsync() ?? Country.Create("Egypt", "EGP", "ar", false);
        if (db.Entry(country).State == EntityState.Detached) db.Countries.Add(country);
        for (var i = 0; i < count; i++)
        {
            var order = Order.Create(null, country.Id, $"{prefix}-{i:D2}", "Addr", "+20", null, PaymentMethod.CashOnDelivery, "Guest", currency: "EGP", city: "القاهرة");
            order.AddItem(Guid.NewGuid(), 1, Money.FromDecimal(100));
            order.CreatedAt = start.AddMinutes(i);
            db.Orders.Add(order);
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Refused_Orders_Back_Off_And_Do_Not_Hold_Back_New_Ones()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        // A full batch (20) of old orders the CRM refuses, then a new order behind them.
        await SeedOrdersAsync(Db(), now.AddHours(-3), 20, "OLD");
        await SeedOrdersAsync(Db(), now.AddMinutes(-1), 1, "NEW");
        crm.Refused.UnionWith(Enumerable.Range(0, 20).Select(i => $"OLD-{i:D2}"));

        // First pass: the 20 old orders fill the batch and fail.
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        var old = await Db().Orders.FirstAsync(o => o.OrderNumber == "OLD-00");
        Assert.Equal(1, old.SyncAttempts);
        Assert.Equal(now.AddSeconds(30), old.NextSyncAttemptAt);
        Assert.Equal("Unknown product code", old.LastSyncError);

        // Next pass (15 s later): the old ones are not due, so the new order goes through. Before,
        // the same 20 were picked again, every pass, and the new order was never sent.
        crm.Received.Clear();
        await sync.ProcessPendingOrdersAsync(now.AddSeconds(15), CancellationToken.None);
        Assert.Equal(new[] { "NEW-00" }, crm.Received);
        Assert.True((await Db().Orders.FirstAsync(o => o.OrderNumber == "NEW-00")).IsSynced);

        // At 30 s they are due again and wait longer after this second failure.
        await sync.ProcessPendingOrdersAsync(now.AddSeconds(30), CancellationToken.None);
        old = await Db().Orders.FirstAsync(o => o.OrderNumber == "OLD-00");
        Assert.Equal(2, old.SyncAttempts);
        Assert.Equal(now.AddSeconds(30).AddMinutes(1), old.NextSyncAttemptAt);

        // Once the CRM accepts it (e.g. the product code was fixed), the order is synced.
        crm.Refused.Remove("OLD-00");
        await sync.ProcessPendingOrdersAsync(now.AddMinutes(2), CancellationToken.None);
        old = await Db().Orders.FirstAsync(o => o.OrderNumber == "OLD-00");
        Assert.True(old.IsSynced);
        Assert.Null(old.NextSyncAttemptAt);
        Assert.Null(old.LastSyncError);
    }

    [Fact]
    public async Task A_Disabled_Crm_Link_Does_Not_Count_Against_The_Orders()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        await SeedOrdersAsync(Db(), now.AddMinutes(-5), 3, "ORD");
        crm.LinkError = new Error("SyncDisabled", "Sync is disabled in configuration.");

        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);

        // Tried once, then the pass stopped; nothing was counted or postponed.
        Assert.Single(crm.Received);
        Assert.All(await Db().Orders.ToListAsync(), o =>
        {
            Assert.False(o.IsSynced);
            Assert.Equal(0, o.SyncAttempts);
            Assert.Null(o.NextSyncAttemptAt);
        });
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(7, 1920)]
    [InlineData(8, 3600)]
    [InlineData(100, 3600)]
    public void Retry_Delay_Doubles_Up_To_An_Hour(int attempt, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), OrderSyncBackgroundService.RetryDelay(attempt));

    [Fact]
    public void The_Crm_Gets_The_Order_Time_In_Istanbul_Time()
    {
        // 22:30 UTC = 01:30 the next day in Istanbul: the CRM must file it under the 25th.
        var crmTime = OrderSyncBackgroundService.ToCrmTime(new DateTime(2026, 9, 24, 22, 30, 0));
        Assert.Equal(new DateTime(2026, 9, 25, 1, 30, 0), crmTime);
        // Sent without an offset, as the CRM's own dates are.
        Assert.Equal(DateTimeKind.Unspecified, crmTime.Kind);
    }

    [Fact]
    public async Task The_Synced_Order_Carries_Istanbul_Time()
    {
        CrmOrderSyncDto? sent = null;
        var capture = new CapturingCrm(dto => sent = dto);
        var (sync, Db, _) = Setup(capture);
        var placedUtc = new DateTime(2026, 9, 24, 21, 15, 0, DateTimeKind.Utc);
        await SeedOrdersAsync(Db(), placedUtc, 1, "ORD");

        await sync.ProcessPendingOrdersAsync(placedUtc.AddMinutes(1), CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal(new DateTime(2026, 9, 25, 0, 15, 0), sent!.CreatedAt);
    }

    private const string ReceiptUrl = "https://loxxking.com/uploads/bank-transfers/0a1b2c.png";

    /// <summary>One bank-transfer order with the receipt its customer uploaded at checkout.</summary>
    private async Task<(Guid OrderId, Guid TransferId)> SeedReceiptAsync(ApplicationDbContext db, DateTime placed, string receiptUrl = ReceiptUrl, bool fileExists = true)
    {
        var country = Country.Create("Egypt", "EGP", "ar", false);
        db.Countries.Add(country);
        var order = Order.Create(null, country.Id, "BANK-01", "Addr", "+20", null, PaymentMethod.BankTransfer, "Guest", currency: "EGP", city: "القاهرة");
        order.AddItem(Guid.NewGuid(), 1, Money.FromDecimal(100));
        order.CreatedAt = placed;
        db.Orders.Add(order);
        var transfer = BankTransfer.Create(order.Id, receiptUrl);
        db.BankTransfers.Add(transfer);
        await db.SaveChangesAsync();
        if (fileExists) _storage.Files.Add(receiptUrl);
        return (order.Id, transfer.Id);
    }

    [Fact]
    public async Task A_Receipt_Follows_Its_Order_To_The_Crm_Once()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (orderId, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1));

        // The CRM does not have the order yet: there is nothing to attach the receipt to.
        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);
        Assert.Empty(crm.Receipts);

        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);
        Assert.Equal(new[] { (orderId, "0a1b2c.png", "image/png") }, crm.Receipts);
        var transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Equal(now, transfer.CrmReceiptSentAt);
        Assert.Equal(0, transfer.CrmReceiptAttempts);

        // Sent is sent: the next passes leave it alone.
        await sync.ProcessPendingReceiptsAsync(now.AddSeconds(15), CancellationToken.None);
        Assert.Single(crm.Receipts);
    }

    [Fact]
    public async Task A_Receipt_The_Crm_Could_Not_Take_Now_Is_Retried_On_The_Order_Backoff()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (_, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1));
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        crm.ReceiptError = new Error("HttpError", "HTTP 500: storage down");

        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);
        var transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Null(transfer.CrmReceiptSentAt);
        Assert.Equal(1, transfer.CrmReceiptAttempts);
        Assert.Equal(now.AddSeconds(30), transfer.CrmReceiptNextAttemptAt);
        Assert.Equal("HTTP 500: storage down", transfer.CrmReceiptLastError);

        // Not before its time, then through once the CRM is back.
        await sync.ProcessPendingReceiptsAsync(now.AddSeconds(15), CancellationToken.None);
        Assert.Single(crm.Receipts);
        crm.ReceiptError = null;
        await sync.ProcessPendingReceiptsAsync(now.AddSeconds(30), CancellationToken.None);
        transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Equal(now.AddSeconds(30), transfer.CrmReceiptSentAt);
        Assert.Null(transfer.CrmReceiptNextAttemptAt);
        Assert.Null(transfer.CrmReceiptLastError);
    }

    [Fact]
    public async Task A_Receipt_The_Crm_Refuses_As_A_File_Waits_A_Day()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (_, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1), "https://loxxking.com/uploads/bank-transfers/0a1b2c.pdf");
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        crm.ReceiptError = new Error("ReceiptRejected", "HTTP 400: The receipt must be a JPG, PNG or WEBP image.");

        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);

        Assert.Equal("application/pdf", Assert.Single(crm.Receipts).ContentType);
        var transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Equal(now.AddDays(1), transfer.CrmReceiptNextAttemptAt);
    }

    [Fact]
    public async Task A_Receipt_Missing_From_The_Disk_Is_Not_Sent_And_Waits_A_Day()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (_, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1), fileExists: false);
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);

        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);

        Assert.Empty(crm.Receipts);
        var transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.CrmReceiptAttempts);
        Assert.Equal(now.AddDays(1), transfer.CrmReceiptNextAttemptAt);
    }

    [Fact]
    public async Task A_Receipt_Already_Reviewed_In_The_Store_Is_Not_Sent()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (_, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1));
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        var db = Db();
        (await db.BankTransfers.FirstAsync(t => t.Id == transferId)).Approve();
        await db.SaveChangesAsync();

        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);

        Assert.Empty(crm.Receipts);
    }

    [Fact]
    public async Task A_Disabled_Crm_Link_Does_Not_Count_Against_The_Receipts()
    {
        var (sync, Db, crm) = Setup();
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var (_, transferId) = await SeedReceiptAsync(Db(), now.AddMinutes(-1));
        await sync.ProcessPendingOrdersAsync(now, CancellationToken.None);
        crm.LinkError = new Error("SyncDisabled", "Sync is disabled in configuration.");

        await sync.ProcessPendingReceiptsAsync(now, CancellationToken.None);

        var transfer = await Db().BankTransfers.FirstAsync(t => t.Id == transferId);
        Assert.Null(transfer.CrmReceiptSentAt);
        Assert.Equal(0, transfer.CrmReceiptAttempts);
        Assert.Null(transfer.CrmReceiptNextAttemptAt);
    }
}
