using loxxking_backend_clean.Application.Common.Caching;
using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.Offers;
using loxxking_backend_clean.Application.Features.Offers.Commands.DeleteOffer;
using loxxking_backend_clean.Application.Features.Offers.Commands.UpdateOffer;
using loxxking_backend_clean.Application.Features.Offers.Queries.GetOffers;
using loxxking_backend_clean.Application.Features.ProductPrices.Commands.UpsertProductPrice;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProducts;
using loxxking_backend_clean.Domain.Entities.Categories;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Offers;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Domain.ValueObjects;
using loxxking_backend_clean.Infrastructure.Persistence;
using loxxking_backend_clean.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

/// <summary>
/// A saved change to the catalogue reaches the storefront at once: the cached reads turn over (including the changes
/// that used to leave them stale for minutes: editing or deleting an offer, a country price) and every open
/// page is told to reload.
/// </summary>
public class VerifyCatalogChangeRuntimeBehavior
{
    private static IDistributedCache GetCache()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions());
        return new MemoryDistributedCache(opts);
    }

    /// <summary>Records what would be broadcast to the open storefronts; null in the list = "any product".</summary>
    private sealed class RecordingNotifier : ICatalogChangeNotifier
    {
        public List<IReadOnlyCollection<Guid>?> Sent { get; } = new();

        public Task CatalogChangedAsync(IReadOnlyCollection<Guid>? productIds)
        {
            Sent.Add(productIds);
            return Task.CompletedTask;
        }
    }

    private static ApplicationDbContext GetDbContext(IDistributedCache cache, ICatalogChangeNotifier? notifier = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(new CatalogChangeInterceptor(cache, notifier ?? new RecordingNotifier()))
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static async Task<Product> SeedProductAsync(ApplicationDbContext db)
    {
        var category = Category.Create("Cat", "Cat", "cat", "");
        var product = Product.Create(category.Id, "Prod", "Prod", "", "prod", Money.FromDecimal(100m), null, new(), new(), new());
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task Editing_Or_Deleting_An_Offer_Shows_On_The_Offers_Page_At_Once()
    {
        var cache = GetCache();
        var db = GetDbContext(cache);
        var product = await SeedProductAsync(db);
        var offer = Offer.Create(product.Id, Percentage.FromDecimal(20), DateRange.Create(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(1)));
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var getOffers = new GetOffersHandler(db, cache);
        Assert.Equal(80m, (await getOffers.Handle(new GetOffersQuery(true), CancellationToken.None)).Value.Single().Price);

        await new UpdateOfferHandler(db).Handle(
            new UpdateOfferCommand(offer.Id, 50m, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(1)), CancellationToken.None);
        Assert.Equal(50m, (await getOffers.Handle(new GetOffersQuery(true), CancellationToken.None)).Value.Single().Price);

        db.ChangeTracker.Clear(); // a new request: the delete loads the offer afresh
        await new DeleteOfferHandler(db).Handle(new DeleteOfferCommand(offer.Id), CancellationToken.None);
        Assert.Empty((await getOffers.Handle(new GetOffersQuery(true), CancellationToken.None)).Value);
    }

    [Fact]
    public async Task A_Country_Price_Change_Shows_In_The_Product_List_At_Once()
    {
        var cache = GetCache();
        var db = GetDbContext(cache);
        var product = await SeedProductAsync(db);
        var country = Country.Create("Egypt", "EGP");
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var getProducts = new GetProductsHandler(db, cache);
        var before = (await getProducts.Handle(new GetProductsQuery(null), CancellationToken.None)).Value.Single();
        Assert.Empty(before.CountryPrices ?? new());

        await new UpsertProductPriceHandler(db).Handle(new UpsertProductPriceCommand(product.Id, country.Id, 750m), CancellationToken.None);

        var after = (await getProducts.Handle(new GetProductsQuery(null), CancellationToken.None)).Value.Single();
        Assert.Equal(750m, Assert.Single(after.CountryPrices!).Price);
    }

    [Fact]
    public async Task Inside_A_Transaction_The_Cache_Turns_Over_And_Storefronts_Hear_Only_On_Commit()
    {
        var cache = GetCache();
        var notifier = new RecordingNotifier();
        var db = GetDbContext(cache, notifier);
        var product = await SeedProductAsync(db);
        notifier.Sent.Clear();
        var keyBefore = await CatalogCache.KeyAsync(cache, "x", CancellationToken.None);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            product.UpdateStock(3);
            await db.SaveChangesAsync();
            // Not committed yet: a read now would still see the old stock, so nothing may move.
            Assert.Equal(keyBefore, await CatalogCache.KeyAsync(cache, "x", CancellationToken.None));
            Assert.Empty(notifier.Sent);
            await tx.CommitAsync();
        }

        Assert.NotEqual(keyBefore, await CatalogCache.KeyAsync(cache, "x", CancellationToken.None));
        Assert.Equal(new[] { product.Id }, Assert.Single(notifier.Sent));
    }

    [Fact]
    public async Task A_Rolled_Back_Change_Is_Never_Broadcast()
    {
        var notifier = new RecordingNotifier();
        var db = GetDbContext(GetCache(), notifier);
        var product = await SeedProductAsync(db);
        notifier.Sent.Clear();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            product.UpdateStock(3);
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task Storefronts_Hear_Which_Product_Changed_Or_That_Any_May_Have()
    {
        var notifier = new RecordingNotifier();
        var db = GetDbContext(GetCache(), notifier);
        var product = await SeedProductAsync(db);
        // A new product arrives with its category: a category can change any card.
        Assert.Null(Assert.Single(notifier.Sent));
        notifier.Sent.Clear();

        var country = Country.Create("Egypt", "EGP");
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        notifier.Sent.Clear();

        await new UpsertProductPriceHandler(db).Handle(new UpsertProductPriceCommand(product.Id, country.Id, 750m), CancellationToken.None);
        Assert.Equal(new[] { product.Id }, Assert.Single(notifier.Sent));
    }

    [Fact]
    public async Task A_Change_Outside_The_Catalogue_Leaves_The_Cache_And_The_Storefronts_Alone()
    {
        var cache = GetCache();
        var notifier = new RecordingNotifier();
        var db = GetDbContext(cache, notifier);
        await SeedProductAsync(db);
        notifier.Sent.Clear();
        var keyBefore = await CatalogCache.KeyAsync(cache, "x", CancellationToken.None);

        db.SiteVisits.Add(loxxking_backend_clean.Domain.Entities.SiteVisits.SiteVisit.Create(null, "/"));
        await db.SaveChangesAsync();

        Assert.Equal(keyBefore, await CatalogCache.KeyAsync(cache, "x", CancellationToken.None));
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task A_Scheduled_Offer_Starting_Or_Ending_Tells_The_Open_Pages()
    {
        var cache = GetCache();
        var db = GetDbContext(cache);
        var product = await SeedProductAsync(db);
        var t = new DateTime(2026, 9, 24, 18, 0, 0, DateTimeKind.Utc);
        db.Offers.Add(Offer.Create(product.Id, Percentage.FromDecimal(20), DateRange.Create(t, t.AddHours(2))));
        await db.SaveChangesAsync();

        var notifier = new RecordingNotifier();
        Task<bool> Window(DateTime after, DateTime upTo) =>
            OfferSchedule.PublishStartsAndEndsAsync(db, cache, notifier, after, upTo, CancellationToken.None);

        // Nothing starts or ends before the offer.
        Assert.False(await Window(t.AddSeconds(-30), t.AddSeconds(-15)));
        Assert.Empty(notifier.Sent);

        // The offer starts: a new catalogue version, and its product's pages reload.
        var keyBefore = await CatalogCache.KeyAsync(cache, "x", CancellationToken.None);
        Assert.True(await Window(t.AddSeconds(-15), t));
        Assert.Equal(product.Id, Assert.Single(Assert.Single(notifier.Sent)!));
        Assert.NotEqual(keyBefore, await CatalogCache.KeyAsync(cache, "x", CancellationToken.None));

        // While it runs, nothing.
        Assert.False(await Window(t, t.AddSeconds(15)));
        Assert.False(await Window(t.AddHours(2).AddSeconds(-15), t.AddHours(2)));

        // It ends: told again.
        Assert.True(await Window(t.AddHours(2), t.AddHours(2).AddSeconds(15)));
        Assert.Equal(2, notifier.Sent.Count);
        Assert.Equal(product.Id, Assert.Single(notifier.Sent[1]!));
    }
}
