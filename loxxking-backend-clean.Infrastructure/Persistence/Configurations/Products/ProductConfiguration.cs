using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Infrastructure.Persistence.Configurations.Products;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        // Deleting a product only flags it: order lines, inventory, prices and reviews all point at it
        // and are restricted, so a real delete failed with a 500. The filter hides it from the store
        // and the dashboard; the order sync and the seeder opt out with IgnoreQueryFilters.
        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.Property(p => p.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(p => p.NameEn).IsRequired().HasMaxLength(200);

        // Luxira/CRM product code sent with synced order lines (G3.1).
        builder.Property(p => p.ProductCode).HasMaxLength(64);
        
        builder.Property(p => p.BasePrice)
               .HasConversion(
                   v => v.Value,
                   v => loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v));
               
        builder.Property(p => p.OriginalPrice)
               .HasConversion(
                   v => v != null ? v.Value : (decimal?)null,
                   v => v.HasValue ? loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v.Value) : null);
        
        builder.HasOne(p => p.Category)
               .WithMany()
               .HasForeignKey(p => p.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
