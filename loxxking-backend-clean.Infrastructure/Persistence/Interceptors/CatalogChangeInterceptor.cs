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
/// Whenever a saved change touches a row the storefront shows — a product (stock and rating included),
/// its prices, an offer, a bundle, a category (its name is on every product card) or a country (its
/// currency and active flag pick the prices) — this starts a new catalogue cache version (see
/// <see cref="CatalogCache"/>) and tells every open storefront to reload (<see cref="ICatalogChangeNotifier"/>).
/// Watching the saves, rather than each command, also covers the paths that never cleared anything:
/// an order taking stock, a review moving the rating, and any command added later.
///
/// Inside an explicit transaction (checkout) both wait for the commit: a read between the save and the
/// commit would still see the old rows, cache them under the new version, and show them to the visitor.
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
    private readonly ICatalogChangeNotifier _notifier;

    // What the save being run now changes (null: nothing in the catalogue).
    private CatalogChange? _saving;
    // Saved, but its transaction has not committed yet.
    private CatalogChange? _awaitingCommit;

    public CatalogChangeInterceptor(IDistributedCache cache, ICatalogChangeNotifier notifier)
    {
        _cache = cache;
        _notifier = notifier;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        _saving = Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _saving = Collect(eventData.Context);
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

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = null;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = null;
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => AfterCommitAsync().GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => AfterCommitAsync();

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _awaitingCommit = null;

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit = null;
        return Task.CompletedTask;
    }

    private async Task AfterSaveAsync(DbContext? context)
    {
        if (_saving is not { } change) return;
        _saving = null;

        if (context?.Database.CurrentTransaction is not null)
        {
            _awaitingCommit = _awaitingCommit is null ? change : _awaitingCommit.Merge(change);
            return;
        }

        await PublishAsync(change);
    }

    private async Task AfterCommitAsync()
    {
        if (_awaitingCommit is not { } change) return;
        _awaitingCommit = null;
        await PublishAsync(change);
    }

    // The data is already saved, so the caller's request must not be cancelled here.
    private async Task PublishAsync(CatalogChange change)
    {
        await CatalogCache.InvalidateAsync(_cache, CancellationToken.None);
        await _notifier.CatalogChangedAsync(change.AnyProduct ? null : change.ProductIds);
    }

    private static CatalogChange? Collect(DbContext? context)
    {
        if (context is null) return null;

        CatalogChange? change = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            // An owned value (an offer's ActivePeriod) is tracked as its own entry: judge it by its owner.
            var type = entry.Metadata;
            while (type.FindOwnership() is { } ownership) type = ownership.PrincipalEntityType;
            if (!CatalogTypes.Contains(type.ClrType)) continue;

            change ??= new CatalogChange();
            var productId = entry.Entity switch
            {
                Product p => p.Id,
                ProductPrice pp => pp.ProductId,
                Offer o => o.ProductId,
                OfferProduct op => op.ProductId,
                _ => (Guid?)null
            };
            if (productId is { } id) change.ProductIds.Add(id);
            // A category, a country or a bundle can change any product card (and an owned value does
            // not say which row owns it): every open page reloads.
            else change.AnyProduct = true;
        }
        return change;
    }

    private sealed class CatalogChange
    {
        public HashSet<Guid> ProductIds { get; } = [];
        public bool AnyProduct { get; set; }

        public CatalogChange Merge(CatalogChange other)
        {
            ProductIds.UnionWith(other.ProductIds);
            AnyProduct |= other.AnyProduct;
            return this;
        }
    }
}
