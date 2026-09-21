namespace loxxking_backend_clean.Application.Features.Products.Commands.CreateProduct;

public record CreateProductCommand(
    Guid CategoryId,
    string NameAr,
    string NameEn,
    string Description,
    List<string> Images,
    string? Features,
    string? ShippingPolicy,
    string? ReturnPolicy,
    decimal BasePrice,
    string? ProductCode = null,
    // Per-country pricing (2026-09-21): BasePrice is the international USD price.
    decimal? InternationalOriginalPrice = null,
    List<CountryPriceInput>? CountryPrices = null
) : IRequest<Result<CreateProductResponse>>;
