using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace loxxking_backend_clean.Application.Features.Products.Queries.GetProduct;

public class GetProductHandler : IRequestHandler<GetProductQuery, Result<GetProductResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public GetProductHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<GetProductResponse>> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var cacheKey = $"ProductDetail_{request.Id}_{lang}";

        var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedProduct = JsonSerializer.Deserialize<GetProductResponse>(cachedData);
            if (cachedProduct != null) return Result.Success(cachedProduct);
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .Where(p => p.Id == request.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return Result.Failure<GetProductResponse>(new Error("Error.NotFound", "Product_NotFound"));
        }

        var response = new GetProductResponse(
            product.Id,
            product.Slug,
            product.NameEn,
            product.NameAr,
            product.Description,
            product.Description,
            product.BasePrice.Value,
            product.OriginalPrice != null ? product.OriginalPrice.Value : null,
            product.Images,
            product.Category.NameEn,
            product.Sizes,
            product.SizeChartJson,
            product.Stock,
            product.Rating,
            product.ReviewCount,
            product.IsNew,
            product.IsBestSeller,
            product.Badge,
            product.Colors,
            product.VideoUrl,
            product.InternationalPrice,
            product.InternationalOriginalPrice,
            await ProductPriceReader.ForProductAsync(_context, product.Id, cancellationToken)
        );

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), options, cancellationToken);

        return Result.Success(response);
    }
}
