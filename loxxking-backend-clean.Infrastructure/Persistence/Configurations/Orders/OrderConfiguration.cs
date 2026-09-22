using loxxking_backend_clean.Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace loxxking_backend_clean.Infrastructure.Persistence.Configurations.Orders;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
    
        builder.HasQueryFilter(o => !o.IsDeleted);

        builder.Property(o => o.TotalAmount).HasConversion(v => v.Value, v => loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v));
        builder.Property(o => o.Subtotal).HasConversion(v => v.Value, v => loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v));
        builder.Property(o => o.Shipping).HasConversion(v => v.Value, v => loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v));
        builder.Property(o => o.Discount).HasConversion(v => v.Value, v => loxxking_backend_clean.Domain.ValueObjects.Money.UnsafeFromDatabase(v));

        builder.HasIndex(o => o.ShipmentCode).IsUnique();
        builder.HasIndex(o => new { o.CountryId, o.Status, o.PaymentMethod, o.CreatedAt });
        builder.Property(o => o.IsSynced).HasDefaultValue(false);
        builder.Property(o => o.Currency).IsRequired().HasMaxLength(10);
        builder.HasIndex(o => o.IsSynced);
    }
}
