namespace loxxking_backend_clean.Application.Features.Products.Queries.GetProduct;

public record GetProductResponse(
    Guid Id, 
    string Slug,
    string NameEn, 
    string NameAr, 
    string DescEn,
    string DescAr,
    decimal Price,
    decimal? OriginalPrice,
    List<string> Images,
    string Category,
    List<string> Sizes,
    string? SizeChart,
    int Stock,
    double Rating,
    int ReviewCount,
    bool IsNew,
    bool IsBestSeller,
    string? Badge,
    List<string> Colors,
    string? VideoUrl = null,
    // Per-country pricing (2026-09-21). Price/OriginalPrice above are the legacy single price;
    // the storefront shows the visitor's country price, else the international USD price.
    decimal? InternationalPrice = null,
    decimal? InternationalOriginalPrice = null,
    List<CountryPriceDto>? CountryPrices = null
);
