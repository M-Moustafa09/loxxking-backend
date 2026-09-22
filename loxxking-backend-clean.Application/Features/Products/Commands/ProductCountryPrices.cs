using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Application.Features.Products.Commands;

/// <summary>One row of the product form's «الأسعار حسب الدولة» list, in that country's currency.</summary>
public record CountryPriceInput(Guid CountryId, decimal Price, decimal? OriginalPrice = null);

/// <summary>
/// Per-country pricing shared by the create and update commands (owner decisions 2026-09-21):
/// an international USD price is required, and each listed country may have its own price.
/// </summary>
public static class ProductCountryPrices
{
    public static Error? ValidateInternational(decimal price, decimal? originalPrice)
    {
        if (price <= 0)
            return new Error("Error.Validation", "Product_InternationalPriceRequired");
        if (originalPrice is decimal original && original > 0 && original <= price)
            return new Error("Error.Validation", "Product_CountryPriceInvalid");
        return null;
    }

    /// <summary>
    /// Replaces the product's country prices with <paramref name="inputs"/>. Null leaves them as
    /// they are (a caller that does not send prices must not wipe them).
    /// </summary>
    public static async Task<Error?> ReplaceAsync(
        IApplicationDbContext context,
        Guid productId,
        IReadOnlyCollection<CountryPriceInput>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs == null)
            return null;

        if (inputs.Select(i => i.CountryId).Distinct().Count() != inputs.Count)
            return new Error("Error.Validation", "Product_CountryPriceDuplicate");

        if (inputs.Any(i => i.Price <= 0 || (i.OriginalPrice is decimal o && o > 0 && o <= i.Price)))
            return new Error("Error.Validation", "Product_CountryPriceInvalid");

        var countryIds = inputs.Select(i => i.CountryId).ToList();
        var soldCount = await context.Countries
            .CountAsync(c => countryIds.Contains(c.Id) && c.IsActive && !c.IsDeleted, cancellationToken);
        if (soldCount != countryIds.Count)
            return new Error("Error.Validation", "Product_CountryNotSold");

        var existing = await context.ProductPrices
            .Where(p => p.ProductId == productId)
            .ToListAsync(cancellationToken);
        context.ProductPrices.RemoveRange(existing);

        foreach (var input in inputs)
        {
            context.ProductPrices.Add(new ProductPrice
            {
                ProductId = productId,
                CountryId = input.CountryId,
                Price = input.Price,
                OriginalPrice = input.OriginalPrice is > 0 ? input.OriginalPrice : null
            });
        }

        return null;
    }
}
