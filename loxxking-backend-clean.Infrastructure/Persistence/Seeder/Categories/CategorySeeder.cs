using loxxking_backend_clean.Domain.Entities.Categories;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Persistence.Seeder.Categories;

public class CategorySeeder : IDataSeeder
{
    public int Order => 4;

    public async Task SeedAsync(SeedContext context, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = serviceProvider.GetRequiredService<ILogger<CategorySeeder>>();

        var categoriesToSeed = new (string NameAr, string NameEn, string Slug, string ImageUrl)[]
        {
            ("ملابس رجالية", "Men's Apparel", "mens-apparel", "https://images.unsplash.com/photo-1617137984095-74e4e5e3613f"),
            ("أحذية فاخرة", "Luxury Shoes", "luxury-shoes", "https://images.unsplash.com/photo-1542291026-7eec264c27ff"),
            ("إكسسوارات وساعات", "Accessories & Watches", "accessories-watches", "https://images.unsplash.com/photo-1523275335684-37898b6baf30")
        };

        foreach (var (nameAr, nameEn, slug, imageUrl) in categoriesToSeed)
        {
            // IgnoreQueryFilters: a seed category the owner deleted must stay deleted, not come back on the next --seed.
            var existing = await dbContext.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Slug == slug, cancellationToken);
            if (existing == null)
            {
                var category = Category.Create(nameAr, nameEn, slug, imageUrl);
                await dbContext.Categories.AddAsync(category, cancellationToken);
                logger.LogInformation("Seeded category '{NameEn}' ({Slug}).", nameEn, slug);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        context.Categories = await dbContext.Categories.IgnoreQueryFilters().ToListAsync(cancellationToken);
    }
}
