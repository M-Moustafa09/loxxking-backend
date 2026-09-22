using loxxking_backend_clean.Domain.Entities.Products;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Products.Commands.CreateProduct;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<CreateProductResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDistributedCache _cache;

    public CreateProductHandler(IApplicationDbContext context, IFileStorageService fileStorageService, IDistributedCache cache)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _cache = cache;
    }

    public async Task<Result<CreateProductResponse>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<CreateProductResponse>(new Error("Error.NotFound", "Category_NotFound"));
        }

        if (await ProductCodeGuard.IsTakenAsync(_context, request.ProductCode, null, cancellationToken))
        {
            return Result.Failure<CreateProductResponse>(new Error("Error.Validation", "Product_CodeInUse"));
        }

        var priceError = ProductCountryPrices.ValidateInternational(request.BasePrice, request.InternationalOriginalPrice);
        if (priceError != null)
        {
            return Result.Failure<CreateProductResponse>(priceError);
        }

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

        var slug = request.NameEn.ToLower().Replace(" ", "-") + "-" + Guid.NewGuid().ToString().Substring(0, 8);

        var product = Product.Create(
            categoryId: request.CategoryId,
            nameAr: request.NameAr,
            nameEn: request.NameEn,
            description: request.Description,
            slug: slug,
            basePrice: loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(request.BasePrice),
            originalPrice: null,
            images: imageUrls,
            sizes: new List<string>(),
            colors: new List<string>(),
            features: request.Features,
            shippingPolicy: request.ShippingPolicy,
            returnPolicy: request.ReturnPolicy
        );

        // Luxira/CRM product code, chosen from the dashboard dropdown (G3.1).
        product.SetProductCode(request.ProductCode);
        product.SetInternationalPrice(request.BasePrice, request.InternationalOriginalPrice);

        _context.Products.Add(product);

        var countryPriceError = await ProductCountryPrices.ReplaceAsync(_context, product.Id, request.CountryPrices, cancellationToken);
        if (countryPriceError != null)
        {
            return Result.Failure<CreateProductResponse>(countryPriceError);
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync("ProductsList_v2_", cancellationToken);
        await _cache.RemoveAsync($"ProductsList_v2_{product.CategoryId}", cancellationToken);

        return Result.Success(new CreateProductResponse(product.Id));
    }
}
