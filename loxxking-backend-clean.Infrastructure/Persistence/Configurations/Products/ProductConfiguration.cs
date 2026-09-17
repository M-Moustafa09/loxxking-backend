using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Infrastructure.Persistence.Configurations.Products;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        
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
