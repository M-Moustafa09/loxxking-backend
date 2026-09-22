using System.Text.Json;
using loxxking_backend_clean.Application.Common.Caching;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProduct;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProducts;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Offers;

/// <summary>A product's offer that is running right now.</summary>
public record ActiveOfferDto(Guid ProductId, decimal Percent, DateTime EndsAt);

/// <summary>
/// The one place that decides which product offers are running and what they take off (owner
/// decisions 2026-09-22): the discount is a percentage of the product's price in the visitor's
/// country, it shows everywhere in the store, and checkout charges it. The product reads and
/// CreateOrderHandler both ask here, so the price shown and the price charged cannot drift apart.
///
/// The product reads are cached for 10–30 minutes, but an offer starts and ends at a set time. So
/// the offer is not baked into those cached responses: it is laid over them on every read from this
/// small map. The map is cached only until the next offer starts or ends (at most a minute), and any
/// saved change to an offer starts a new catalogue version (CatalogCache), so it is never stale.
/// </summary>
public static class ActiveOffers
{
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromMinutes(1);

    public static async Task<Dictionary<Guid, ActiveOfferDto>> GetAsync(
        IApplicationDbContext context, IDistributedCache cache, CancellationToken cancellationToken)
    {
        var cacheKey = await CatalogCache.KeyAsync(cache, "offers:active-map", cancellationToken);
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            var cachedOffers = JsonSerializer.Deserialize<List<ActiveOfferDto>>(cached);
            if (cachedOffers != null) return cachedOffers.ToDictionary(o => o.ProductId);
        }

        var now = DateTime.UtcNow;
        var upcoming = await context.Offers
            .AsNoTracking()
            .Where(o => o.ActivePeriod.EndDate >= now && !o.Product.IsDeleted)
            .Select(o => new { o.ProductId, Percent = o.Discount.Value, o.ActivePeriod.StartDate, o.ActivePeriod.EndDate })
            .ToListAsync(cancellationToken);

        // One offer per product at a time is enforced when an offer is saved; should two ever
        // overlap, the larger discount wins rather than an arbitrary one.
        var active = upcoming
            .Where(o => o.StartDate <= now)
            .GroupBy(o => o.ProductId)
            .Select(g => g.OrderByDescending(o => o.Percent).First())
            .Select(o => new ActiveOfferDto(o.ProductId, o.Percent, DateTime.SpecifyKind(o.EndDate, DateTimeKind.Utc)))
            .ToList();

        // Keep the map only until the next offer starts or a running one ends.
        var nextChange = upcoming
            .Select(o => o.StartDate > now ? o.StartDate : o.EndDate)
            .DefaultIfEmpty(now + MaxCacheAge)
            .Min();
        var ttl = nextChange - now;
        if (ttl > MaxCacheAge) ttl = MaxCacheAge;
        if (ttl < TimeSpan.FromSeconds(1)) ttl = TimeSpan.FromSeconds(1);

        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(active),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);

        return active.ToDictionary(o => o.ProductId);
    }

    /// <summary>
    /// The price after the discount, rounded to the cent. The storefront does the same sum
    /// (product.repository.impl.ts, applyOffer), so the cart and the order agree.
    /// </summary>
    public static decimal Apply(decimal price, decimal percent) =>
        Math.Round(price * (100m - percent) / 100m, 2, MidpointRounding.AwayFromZero);

    public static ProductListResponse WithOffer(this ProductListResponse product, IReadOnlyDictionary<Guid, ActiveOfferDto> offers) =>
        offers.TryGetValue(product.Id, out var offer)
            ? product with { OfferPercent = offer.Percent, OfferEndsAt = offer.EndsAt }
            : product with { OfferPercent = null, OfferEndsAt = null };

    public static GetProductResponse WithOffer(this GetProductResponse product, IReadOnlyDictionary<Guid, ActiveOfferDto> offers) =>
        offers.TryGetValue(product.Id, out var offer)
            ? product with { OfferPercent = offer.Percent, OfferEndsAt = offer.EndsAt }
            : product with { OfferPercent = null, OfferEndsAt = null };
}
