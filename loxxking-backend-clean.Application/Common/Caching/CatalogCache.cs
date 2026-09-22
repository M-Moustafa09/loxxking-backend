using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Common.Caching;

/// <summary>
/// One switch for every cached storefront read that shows product data: product lists and pages,
/// per-country prices, offers and bundles.
///
/// Each command used to remove the keys it knew about, and several missed some: editing or deleting an
/// offer, adding or removing a product image, and every price change left the product list and the
/// offers page showing the old data for up to 10 minutes (a product page by slug up to 30). Now each
/// of those reads puts the current catalogue version in its key, and a saved change to any catalogue
/// row (see CatalogChangeInterceptor in Infrastructure) starts a new version, so the next read goes
/// to the database. Old entries are never read again and fall out when their TTL ends.
/// </summary>
public static class CatalogCache
{
    private const string VersionKey = "catalog:version";

    /// <summary>The cache key for <paramref name="key"/> in the current catalogue version.</summary>
    public static async Task<string> KeyAsync(IDistributedCache cache, string key, CancellationToken cancellationToken)
    {
        var version = await cache.GetStringAsync(VersionKey, cancellationToken);
        if (string.IsNullOrEmpty(version))
        {
            version = NewVersion();
            await cache.SetStringAsync(VersionKey, version, cancellationToken);
        }
        return $"catalog:{version}:{key}";
    }

    /// <summary>Makes every cached catalogue read stale at once.</summary>
    public static Task InvalidateAsync(IDistributedCache cache, CancellationToken cancellationToken)
        => cache.SetStringAsync(VersionKey, NewVersion(), cancellationToken);

    private static string NewVersion() => Guid.NewGuid().ToString("N");
}
