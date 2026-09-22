using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Infrastructure.Persistence.Configurations.Products;

public class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.Price);
        builder.Property(p => p.OriginalPrice).HasPrecision(18, 2);

        // One price per product per country: the product form replaces the whole set on save.
        builder.HasIndex(p => new { p.ProductId, p.CountryId })
               .HasDatabaseName("IX_ProductPrices_ProductId_CountryId");
    }
}
