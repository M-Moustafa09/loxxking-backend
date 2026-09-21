using loxxking_backend_clean.Domain.Entities.Categories;

namespace loxxking_backend_clean.Infrastructure.Persistence.Configurations.Categories;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);
        // Deleted categories stay in the table (their deleted products still point at them) but vanish everywhere.
        builder.HasQueryFilter(c => !c.IsDeleted);
        builder.Property(c => c.NameAr).IsRequired().HasMaxLength(150);
        builder.Property(c => c.NameEn).IsRequired().HasMaxLength(150);
    }
}
