namespace loxxking_backend_clean.Application.Common.Interfaces;

/// <summary>
/// Tells every open storefront that the catalogue changed, so it reloads what it shows without a refresh.
/// Only a signal goes out, never product data: each visitor reloads through the normal API, which picks
/// the price for their own country.
/// </summary>
public interface ICatalogChangeNotifier
{
    /// <param name="productIds">The products that changed, or null when the change can touch any of them
    /// (a category, a country, a bundle).</param>
    Task CatalogChangedAsync(IReadOnlyCollection<Guid>? productIds);
}
