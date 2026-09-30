using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.BankTransfers.Commands.UploadTransfer;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Orders;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

/// <summary>
/// The receipt a customer uploads at checkout is an image, stored under an extension the CRM accepts:
/// the transfer is reviewed in the CRM, which takes nothing else.
/// </summary>
public class VerifyBankTransferUploadRuntimeBehavior
{
    /// <summary>Records the file name each upload was stored under.</summary>
    private sealed class RecordingStorage : IFileStorageService
    {
        public List<string> StoredAs { get; } = new();

        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken cancellationToken)
        {
            StoredAs.Add(fileName);
            return Task.FromResult($"/uploads/{folder}/{fileName}");
        }

        public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream?> OpenReadAsync(string fileUrl, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
    }

    private static async Task<(ApplicationDbContext Db, Guid OrderId)> SeedOrderAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("DataSource=:memory:").Options;
        var db = new ApplicationDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var country = Country.Create("Egypt", "EGP", "ar", false);
        db.Countries.Add(country);
        var order = Order.Create(null, country.Id, "BANK-01", "Addr", "+20", null, PaymentMethod.BankTransfer, "Guest", currency: "EGP", city: "القاهرة");
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (db, order.Id);
    }

    private static UploadTransferCommand Upload(Guid orderId, string fileName, string contentType) =>
        new(orderId, new MemoryStream(new byte[] { 1, 2, 3 }), fileName, contentType, Guid.Empty);

    [Theory]
    [InlineData("application/pdf", "receipt.pdf")]
    [InlineData("image/gif", "receipt.gif")]
    [InlineData("image/svg+xml", "receipt.svg")]
    [InlineData("text/html", "receipt.html")]
    [InlineData("", "receipt.png")]
    public async Task Anything_But_A_Jpg_Png_Or_Webp_Image_Is_Refused(string contentType, string fileName)
    {
        var (db, orderId) = await SeedOrderAsync();
        var storage = new RecordingStorage();

        var result = await new UploadTransferHandler(db, storage).Handle(Upload(orderId, fileName, contentType), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("BankTransfer_OnlyImagesAllowed", result.Error.Message);
        Assert.Empty(storage.StoredAs);
        Assert.Empty(await db.BankTransfers.ToListAsync());
    }

    [Theory]
    [InlineData("image/jpeg", "IMG_0042.JPG", "receipt.jpg")]
    [InlineData("image/png", "screenshot.png", "receipt.png")]
    [InlineData("image/webp", "transfer.webp", "receipt.webp")]
    // The extension follows the type, not the name the customer's file came with.
    [InlineData("image/jpeg", "receipt.jfif", "receipt.jpg")]
    [InlineData("IMAGE/PNG", "receipt.html", "receipt.png")]
    public async Task An_Image_Is_Stored_Under_Its_Types_Extension(string contentType, string fileName, string storedAs)
    {
        var (db, orderId) = await SeedOrderAsync();
        var storage = new RecordingStorage();

        var result = await new UploadTransferHandler(db, storage).Handle(Upload(orderId, fileName, contentType), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { storedAs }, storage.StoredAs);
        var transfer = await db.BankTransfers.SingleAsync();
        Assert.Equal($"/uploads/bank-transfers/{storedAs}", transfer.ProofImageUrl);
    }
}
