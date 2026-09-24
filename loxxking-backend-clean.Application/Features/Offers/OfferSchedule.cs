using loxxking_backend_clean.Application.Common.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Offers;

/// <summary>
/// An offer (or a bundle) starts and ends at a set time, and nothing is saved at that moment, so the
/// save-driven push (CatalogChangeInterceptor) never told the open storefronts: a visitor who opened a
/// page before a scheduled offer started never saw it, and one whose offer had ended kept seeing the
/// discounted price, until a manual refresh. OfferScheduleBackgroundService calls this every few seconds
/// with the time elapsed since its last call; any offer that started or ended in it starts a new
/// catalogue version and tells every open page to reload, exactly as a saved change does.
/// </summary>
public static class OfferSchedule
{
    /// <summary>
    /// Publishes the offers that started or ended in (<paramref name="after"/>, <paramref name="upTo"/>].
    /// An offer runs while StartDate &lt;= now &lt;= EndDate (ActiveOffers), so it starts at StartDate and
    /// has ended once now is past EndDate. Consecutive windows sharing their edge see each moment once.
    /// </summary>
    /// <returns>Whether anything was published.</returns>
    public static async Task<bool> PublishStartsAndEndsAsync(
        IApplicationDbContext context,
        IDistributedCache cache,
        ICatalogChangeNotifier notifier,
        DateTime after,
        DateTime upTo,
        CancellationToken cancellationToken)
    {
        var productIds = await context.Offers
            .AsNoTracking()
            .Where(o => (o.ActivePeriod.StartDate > after && o.ActivePeriod.StartDate <= upTo)
                     || (o.ActivePeriod.EndDate >= after && o.ActivePeriod.EndDate < upTo))
            .Select(o => o.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // A bundle does not name one product: every open page reloads, as for a saved bundle.
        var bundleChanged = await context.BundleOffers
            .AsNoTracking()
            .AnyAsync(b => (b.ActivePeriod.StartDate > after && b.ActivePeriod.StartDate <= upTo)
                        || (b.ActivePeriod.EndDate >= after && b.ActivePeriod.EndDate < upTo), cancellationToken);

        if (productIds.Count == 0 && !bundleChanged) return false;

        await CatalogCache.InvalidateAsync(cache, cancellationToken);
        await notifier.CatalogChangedAsync(bundleChanged ? null : productIds);
        return true;
    }
}
