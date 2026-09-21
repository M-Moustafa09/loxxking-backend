using loxxking_backend_clean.Domain.Entities.Inventory;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Domain.ValueObjects;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Persistence.Seeder.Products;

public class ProductSeeder : IDataSeeder
{
    public int Order => 5;

    public async Task SeedAsync(SeedContext context, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = serviceProvider.GetRequiredService<ILogger<ProductSeeder>>();

        var apparelCategory = context.Categories.First(c => c.Slug == "mens-apparel");
        var shoesCategory = context.Categories.First(c => c.Slug == "luxury-shoes");
        var accessoriesCategory = context.Categories.First(c => c.Slug == "accessories-watches");

        var productsDefs = new (
            string Slug, 
            Guid CategoryId, 
            string NameAr, 
            string NameEn, 
            string Description, 
            decimal BasePrice, 
            decimal? OriginalPrice, 
            List<string> Images, 
            List<string> Sizes, 
            List<string> Colors, 
            string? Badge, 
            int Stock, 
            double Rating, 
            int ReviewCount, 
            bool IsBestSeller, 
            bool IsNew)[]
        {
            (
                "oxford-cotton-shirt",
                apparelCategory.Id,
                "قميص قطن أكسفورد كلاسيكي",
                "Classic Oxford Cotton Shirt",
                "مصنوع من أجود خيوط القطن الطبيعي 100% بتصميم يجمع بين الأناقة الرسمية والراحة اليومية.",
                1250m,
                1500m,
                new List<string> { "https://images.unsplash.com/photo-1602810318383-e386cc2a3ccf" },
                new List<string> { "S", "M", "L", "XL" },
                new List<string> { "White", "Sky Blue", "Navy" },
                "Best Seller",
                75,
                4.8,
                32,
                true,
                false
            ),
            (
                "italian-leather-loafers",
                shoesCategory.Id,
                "حذاء لوفر جلد إيطالي يدوي",
                "Handcrafted Italian Leather Loafers",
                "حذاء لوفر فاخر مصنع يدويًا من الجلد الإيطالي الطبيعي مع نعل مريح مقاوم للانزلاق.",
                2800m,
                3200m,
                new List<string> { "https://images.unsplash.com/photo-1533867617858-e7b97e060509" },
                new List<string> { "41", "42", "43", "44", "45" },
                new List<string> { "Cognac Brown", "Classic Black" },
                "Premium",
                40,
                4.9,
                18,
                true,
                true
            ),
            (
                "minimalist-chronograph-watch",
                accessoriesCategory.Id,
                "ساعة كرونوغراف بتصميم عصري",
                "Minimalist Chronograph Leather Watch",
                "ساعة يد أنيقة بحركة كوارتز يابانية دقيقة وحزام من الجلد الطبيعي وزجاج مقاوم للخدش.",
                3500m,
                4000m,
                new List<string> { "https://images.unsplash.com/photo-1524805444758-089113d48a6d" },
                new List<string> { "42mm" },
                new List<string> { "Silver/Black", "Rose Gold/Brown" },
                "New Arrival",
                25,
                4.7,
                14,
                false,
                true
            )
        };

        foreach (var p in productsDefs)
        {
            // Deleted seed products still count as existing, or every restart would bring them back.
            var existingProduct = await dbContext.Products.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Slug == p.Slug, cancellationToken);
            if (existingProduct == null)
            {
                var product = Product.Create(
                    categoryId: p.CategoryId,
                    nameAr: p.NameAr,
                    nameEn: p.NameEn,
                    description: p.Description,
                    slug: p.Slug,
                    basePrice: Money.FromDecimal(p.BasePrice),
                    originalPrice: p.OriginalPrice.HasValue ? Money.FromDecimal(p.OriginalPrice.Value) : null,
                    images: p.Images,
                    sizes: p.Sizes,
                    colors: p.Colors,
                    features: "خامات أصلية عالية الجودة | ضمان لمدة عام",
                    shippingPolicy: "شحن سريع خلال 2-4 أيام عمل لجميع المحافظات",
                    returnPolicy: "إمكانية الاستبدال والاسترجاع مجاناً خلال 14 يوماً",
                    isNew: p.IsNew,
                    isBestSeller: p.IsBestSeller,
                    badge: p.Badge);

                product.UpdateStock(p.Stock);
                product.UpdateRating(p.Rating, p.ReviewCount);

                await dbContext.Products.AddAsync(product, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Seeded product '{NameEn}' ({Slug}).", p.NameEn, p.Slug);
                existingProduct = product;
            }

            // Seed ProductPrices and InventoryItems across countries for this product
            foreach (var country in context.Countries)
            {
                // Pricing rule: EGP is base, SAR approx / 8, AED approx / 8
                decimal countryPrice = country.Currency switch
                {
                    "SAR" => Math.Round(p.BasePrice / 8m, 0),
                    "AED" => Math.Round(p.BasePrice / 8m, 0),
                    _ => p.BasePrice
                };

                var priceExists = await dbContext.ProductPrices.AnyAsync(
                    pr => pr.ProductId == existingProduct.Id && pr.CountryId == country.Id, 
                    cancellationToken);

                if (!priceExists)
                {
                    var productPrice = new ProductPrice
                    {
                        ProductId = existingProduct.Id,
                        CountryId = country.Id,
                        Price = countryPrice
                    };
                    await dbContext.ProductPrices.AddAsync(productPrice, cancellationToken);
                }

                var inventoryExists = await dbContext.InventoryItems.AnyAsync(
                    inv => inv.ProductId == existingProduct.Id && inv.CountryId == country.Id, 
                    cancellationToken);

                if (!inventoryExists)
                {
                    int countryQty = country.IsDefault ? 50 : 20;
                    var invItem = InventoryItem.Create(existingProduct.Id, country.Id, countryQty);
                    await dbContext.InventoryItems.AddAsync(invItem, cancellationToken);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        context.Products = await dbContext.Products.ToListAsync(cancellationToken);
    }
}
