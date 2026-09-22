using System.Data.Common;
using loxxking_backend_clean.Application.Common.Caching;
using loxxking_backend_clean.Domain.Entities.Categories;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Offers;
using loxxking_backend_clean.Domain.Entities.Products;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Starts a new catalogue cache version (see <see cref="CatalogCache"/>) whenever a saved change touches a
/// row the storefront shows: a product (stock and rating included), its prices, an offer, a bundle, a
/// category (its name is on every product card) or a country (its currency and active flag pick the
/// prices). Watching the saves, rather than each command, also covers the paths that never cleared
/// anything: an order taking stock, a review moving the rating, and any command added later.
///
/// Inside an explicit transaction (checkout) the new version waits for the commit: a read between the
/// save and the commit would still see the old rows and cache them under the new version.
/// Scoped, like the DbContext it is attached to.
/// </summary>
public sealed class CatalogChangeInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly HashSet<Type> CatalogTypes =
    [
        typeof(Product), typeof(ProductPrice),
        typeof(Offer), typeof(OfferProduct), typeof(BundleOffer), typeof(BundleOfferItem),
        typeof(Category), typeof(Country)
    ];

    private readonly IDistributedCache _cache;

    // A catalogue row is in the save being run now.
    private bool _saving;
    // Saved, but its transaction has not committed yet.
    private bool _awaitingCommit;

    public CatalogChangeInterceptor(IDistributedCache cache)
    {
        _cache = cache;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        _saving = TouchesCatalog(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _saving = TouchesCatalog(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSaveAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await AfterSaveAsync(eventData.Context);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = false;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = false;
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => AfterCommitAsync().GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => AfterCommitAsync();

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _awaitingCommit = false;

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit = false;
        return Task.CompletedTask;
    }

    private async Task AfterSaveAsync(DbContext? context)
    {
        if (!_saving) return;
        _saving = false;

        if (context?.Database.CurrentTransaction is not null)
        {
            _awaitingCommit = true;
            return;
        }

        await PublishAsync();
    }

    private async Task AfterCommitAsync()
    {
        if (!_awaitingCommit) return;
        _awaitingCommit = false;
        await PublishAsync();
    }

    // The data is already saved, so the caller's request must not be cancelled here.
    private Task PublishAsync() => CatalogCache.InvalidateAsync(_cache, CancellationToken.None);

    private static bool TouchesCatalog(DbContext? context)
        => context is not null && context.ChangeTracker.Entries().Any(IsCatalogChange);

    private static bool IsCatalogChange(EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) return false;

        // An owned value (an offer's ActivePeriod) is tracked as its own entry: judge it by its owner.
        var type = entry.Metadata;
        while (type.FindOwnership() is { } ownership) type = ownership.PrincipalEntityType;
        return CatalogTypes.Contains(type.ClrType);
    }
}
