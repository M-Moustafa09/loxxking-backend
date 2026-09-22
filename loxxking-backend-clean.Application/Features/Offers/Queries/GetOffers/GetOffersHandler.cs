using Microsoft.Extensions.Caching.Distributed;
using loxxking_backend_clean.Application.Common.Caching;
using System.Text.Json;

using loxxking_backend_clean.Application.Features.Products.Queries.GetProducts;

namespace loxxking_backend_clean.Application.Features.Offers.Queries.GetOffers;

public class GetOffersHandler : IRequestHandler<GetOffersQuery, Result<List<ProductListResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    private const string ActiveOffersCacheKey = "offers:active";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public GetOffersHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<List<ProductListResponse>>> Handle(GetOffersQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = request.ActiveOnly
            ? await CatalogCache.KeyAsync(_cache, ActiveOffersCacheKey, cancellationToken)
            : null;

        if (cacheKey is not null)
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                var cachedOffers = JsonSerializer.Deserialize<List<ProductListResponse>>(cached);
                if (cachedOffers != null) return Result.Success(cachedOffers);
            }
        }

        var query = _context.Offers.AsQueryable();

        if (request.ActiveOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(o => o.ActivePeriod.StartDate <= now && o.ActivePeriod.EndDate >= now);
        }

        var offersList = await query
            .OrderByDescending(o => o.ActivePeriod.StartDate)
            .Select(o => new ProductListResponse(
                o.Product.Id, 
                o.Product.Slug,
                o.Product.NameEn, 
                o.Product.NameAr, 
                o.Product.Description,
                o.Product.Description, 
                o.Product.BasePrice.Value - (o.Product.BasePrice.Value * o.Discount.Value / 100), // Discounted Price
                o.Product.BasePrice.Value, // Original Price
                o.Product.Images,
                o.Product.Category.NameEn,
                o.Product.Sizes,
                o.Product.SizeChartJson,
                o.Product.Stock,
                o.Product.Rating,
                o.Product.ReviewCount,
                o.Product.IsNew,
                o.Product.IsBestSeller,
                $"-{o.Discount.Value}%", // Badge is the discount
                o.Product.Colors
            ))
            .ToListAsync(cancellationToken);

        if (cacheKey is not null)
        {
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl };
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(offersList), options, cancellationToken);
        }

        return Result.Success(offersList);
    }
}
