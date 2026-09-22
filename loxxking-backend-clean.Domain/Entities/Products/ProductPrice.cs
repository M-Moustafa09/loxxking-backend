using loxxking_backend_clean.Domain.Common;
using loxxking_backend_clean.Domain.Entities.Countries;

namespace loxxking_backend_clean.Domain.Entities.Products;

/// <summary>
/// A product's price in one country, in that country's currency (Country.Currency). Set by the
/// admin from the product form; a country without a row shows the product's international USD price.
/// </summary>
public class ProductPrice : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid CountryId { get; set; }
    public Country Country { get; set; } = null!;

    public decimal Price { get; set; }

    /// <summary>Price before the discount, shown struck through. Null = no discount.</summary>
    public decimal? OriginalPrice { get; set; }
}
