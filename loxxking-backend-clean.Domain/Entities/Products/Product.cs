using loxxking_backend_clean.Domain.Entities.Categories;

namespace loxxking_backend_clean.Domain.Entities.Products;

public class Product : BaseEntity
{
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public List<string> Images { get; private set; } = new();
    public string? Features { get; private set; }
    public string? ShippingPolicy { get; private set; }
    public string? ReturnPolicy { get; private set; }
    public loxxking_backend_clean.Domain.ValueObjects.Money BasePrice { get; private set; } = null!;
    
    public string Slug { get; private set; } = string.Empty;
    public loxxking_backend_clean.Domain.ValueObjects.Money? OriginalPrice { get; private set; }
    public List<string> Sizes { get; private set; } = new();
    public string? SizeChartJson { get; private set; }
    public int Stock { get; private set; }
    public double Rating { get; private set; }
    public int ReviewCount { get; private set; }
    public bool IsNew { get; private set; }
    public bool IsBestSeller { get; private set; }
    public string? Badge { get; private set; }
    public List<string> Colors { get; private set; } = new();

    // The Luxira/CRM product code for this item (e.g. "L41"). Assigned from the dashboard (a dropdown
    // sourced from the Luxira product list) and sent with each synced order line so the CRM resolves the
    // item to its warehouse row. Nullable: existing products carry none until an admin assigns one. (G3.1)
    public string? ProductCode { get; private set; }

    // One optional video, shown on the product page after the images. Uploaded on its own endpoint
    // (not inline like the images) because a video is too big to travel inside the JSON body.
    public string? VideoUrl { get; private set; }

    public ICollection<ProductPrice> Prices { get; private set; } = new List<ProductPrice>();

    protected Product() { }

    private Product(
        Guid categoryId, 
        string nameAr, 
        string nameEn, 
        string description, 
        string slug, 
        loxxking_backend_clean.Domain.ValueObjects.Money basePrice, 
        loxxking_backend_clean.Domain.ValueObjects.Money? originalPrice,
        List<string> images,
        List<string> sizes,
        List<string> colors,
        string? features,
        string? shippingPolicy,
        string? returnPolicy,
        string? sizeChartJson,
        bool isNew,
        bool isBestSeller,
        string? badge)
    {
        CategoryId = categoryId;
        NameAr = nameAr;
        NameEn = nameEn;
        Description = description;
        Slug = slug;
        BasePrice = basePrice;
        OriginalPrice = originalPrice;
        Images = images ?? new();
        Sizes = sizes ?? new();
        Colors = colors ?? new();
        Features = features;
        ShippingPolicy = shippingPolicy;
        ReturnPolicy = returnPolicy;
        SizeChartJson = sizeChartJson;
        IsNew = isNew;
        IsBestSeller = isBestSeller;
        Badge = badge;
    }

    public static Product Create(
        Guid categoryId, 
        string nameAr, 
        string nameEn, 
        string description, 
        string slug, 
        loxxking_backend_clean.Domain.ValueObjects.Money basePrice, 
        loxxking_backend_clean.Domain.ValueObjects.Money? originalPrice,
        List<string> images,
        List<string> sizes,
        List<string> colors,
        string? features = null,
        string? shippingPolicy = null,
        string? returnPolicy = null,
        string? sizeChartJson = null,
        bool isNew = false,
        bool isBestSeller = false,
        string? badge = null)
    {
        if (categoryId == Guid.Empty) throw new ArgumentException("Domain_Product_CategoryIdRequired", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(nameAr)) throw new ArgumentException("Domain_Product_NameArRequired", nameof(nameAr));
        if (string.IsNullOrWhiteSpace(nameEn)) throw new ArgumentException("Domain_Product_NameEnRequired", nameof(nameEn));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Domain_Product_SlugRequired", nameof(slug));
        if (basePrice == null) throw new ArgumentNullException(nameof(basePrice), "Domain_Product_BasePriceRequired");

        return new Product(categoryId, nameAr, nameEn, description, slug, basePrice, originalPrice, images, sizes, colors, features, shippingPolicy, returnPolicy, sizeChartJson, isNew, isBestSeller, badge);
    }

    public void UpdateDetails(
        Guid categoryId,
        string nameAr,
        string nameEn,
        string description,
        string slug,
        loxxking_backend_clean.Domain.ValueObjects.Money basePrice,
        loxxking_backend_clean.Domain.ValueObjects.Money? originalPrice,
        List<string> images,
        List<string> sizes,
        List<string> colors,
        string? features,
        string? shippingPolicy,
        string? returnPolicy,
        string? sizeChartJson,
        bool isNew,
        bool isBestSeller,
        string? badge)
    {
        if (categoryId == Guid.Empty) throw new ArgumentException("Domain_Product_CategoryIdRequired", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(nameAr)) throw new ArgumentException("Domain_Product_NameArRequired", nameof(nameAr));
        if (string.IsNullOrWhiteSpace(nameEn)) throw new ArgumentException("Domain_Product_NameEnRequired", nameof(nameEn));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Domain_Product_SlugRequired", nameof(slug));
        if (basePrice == null) throw new ArgumentNullException(nameof(basePrice), "Domain_Product_BasePriceRequired");

        CategoryId = categoryId;
        NameAr = nameAr;
        NameEn = nameEn;
        Description = description;
        Slug = slug;
        BasePrice = basePrice;
        OriginalPrice = originalPrice;
        Images = images ?? new();
        Sizes = sizes ?? new();
        Colors = colors ?? new();
        Features = features;
        ShippingPolicy = shippingPolicy;
        ReturnPolicy = returnPolicy;
        SizeChartJson = sizeChartJson;
        IsNew = isNew;
        IsBestSeller = isBestSeller;
        Badge = badge;
    }

    public void SetProductCode(string? productCode)
    {
        ProductCode = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim();
    }

    public void SetVideoUrl(string? videoUrl)
    {
        VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim();
    }

    public void UpdateStock(int newStock)
    {
        if (newStock < 0) throw new ArgumentException("Domain_Product_StockNegative", nameof(newStock));
        Stock = newStock;
    }

    public void UpdateRating(double rating, int reviewCount)
    {
        if (rating < 0 || rating > 5) throw new ArgumentException("Domain_Product_RatingRange", nameof(rating));
        if (reviewCount < 0) throw new ArgumentException("Domain_Product_ReviewCountNegative", nameof(reviewCount));
        
        Rating = rating;
        ReviewCount = reviewCount;
    }
}
