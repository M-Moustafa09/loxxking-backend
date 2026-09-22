namespace loxxking_backend_clean.Application.Features.Products.Queries;

/// <summary>
/// A product's price in one of the store's countries, in that country's currency. Every product
/// response carries all of them plus the international USD price, and the storefront picks the
/// visitor's own (so the responses stay cacheable per product, not per country).
/// </summary>
public record CountryPriceDto(
    Guid CountryId,
    string? CountryCode,
    string Currency,
    decimal Price,
    decimal? OriginalPrice);

internal static class ProductPriceReader
{
    /// <summary>The product's prices in the countries the store currently sells in.</summary>
    public static Task<List<CountryPriceDto>> ForProductAsync(
        IApplicationDbContext context, Guid productId, CancellationToken cancellationToken) =>
        context.ProductPrices
            .AsNoTracking()
            .Where(pp => pp.ProductId == productId && pp.Country.IsActive && !pp.Country.IsDeleted)
            .Select(pp => new CountryPriceDto(pp.CountryId, pp.Country.Code, pp.Country.Currency, pp.Price, pp.OriginalPrice))
            .ToListAsync(cancellationToken);
}
