namespace loxxking_backend_clean.Application.Features.Products.Commands.UpdateProduct;

public record UpdateProductCommand(
    Guid Id, 
    string NameAr, 
    string NameEn, 
    string Description, 
    List<string> Images,
    string? Features,
    string? ShippingPolicy,
    string? ReturnPolicy,
    decimal BasePrice,
    string? ProductCode = null,
    // Per-country pricing (2026-09-21): BasePrice is the international USD price. CountryPrices
    // replaces the product's whole set; null leaves the stored prices untouched.
    decimal? InternationalOriginalPrice = null,
    List<CountryPriceInput>? CountryPrices = null
) : IRequest<Result>;

public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public UpdateProductHandler(IApplicationDbContext context, IFileStorageService fileStorageService) 
    { 
        _context = context; 
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (product == null) return Result.Failure(new Error("Error.NotFound", "Product_NotFound"));
        if (await ProductCodeGuard.IsTakenAsync(_context, request.ProductCode, product.Id, cancellationToken))
            return Result.Failure(new Error("Error.Validation", "Product_CodeInUse"));

        var priceError = ProductCountryPrices.ValidateInternational(request.BasePrice, request.InternationalOriginalPrice);
        if (priceError != null) return Result.Failure(priceError);

        var countryPriceError = await ProductCountryPrices.ReplaceAsync(_context, product.Id, request.CountryPrices, cancellationToken);
        if (countryPriceError != null) return Result.Failure(countryPriceError);

        var imageUrls = new List<string>();
        if (request.Images != null)
        {
            foreach (var imgStr in request.Images)
            {
                if (imgStr.StartsWith("data:image/"))
                {
                    var base64Data = imgStr.Substring(imgStr.IndexOf(",") + 1);
                    var bytes = Convert.FromBase64String(base64Data);
                    using var stream = new MemoryStream(bytes);
                    var ext = imgStr.Split(';')[0].Split('/')[1];
                    var fileName = $"{Guid.NewGuid()}.{ext}";
                    var contentType = $"image/{ext}";
                    var url = await _fileStorageService.UploadAsync(stream, fileName, contentType, "products", cancellationToken);
                    imageUrls.Add(url);
                }
                else
                {
                    imageUrls.Add(imgStr);
                }
            }
        }

        var updatedImages = request.Images != null ? imageUrls : product.Images;
        
        product.UpdateDetails(
            categoryId: product.CategoryId, // Unchanged in this command
            nameAr: request.NameAr,
            nameEn: request.NameEn,
            description: request.Description,
            slug: product.Slug, // Unchanged in this command
            basePrice: loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(request.BasePrice),
            originalPrice: product.OriginalPrice, // Unchanged in this command
            images: updatedImages,
            sizes: product.Sizes,
            colors: product.Colors,
            features: request.Features,
            shippingPolicy: request.ShippingPolicy,
            returnPolicy: request.ReturnPolicy,
            sizeChartJson: product.SizeChartJson,
            isNew: product.IsNew,
            isBestSeller: product.IsBestSeller,
            badge: product.Badge
        );

        // Luxira/CRM product code, chosen from the dashboard dropdown (G3.1). This command is a full
        // replace (like the fields above), and the dashboard form submits the current code.
        product.SetProductCode(request.ProductCode);
        product.SetInternationalPrice(request.BasePrice, request.InternationalOriginalPrice);

        _context.Products.Update(product);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
