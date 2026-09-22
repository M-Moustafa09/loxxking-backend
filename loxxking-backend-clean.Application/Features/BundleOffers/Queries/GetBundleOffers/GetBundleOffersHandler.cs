using Microsoft.Extensions.Caching.Distributed;
using loxxking_backend_clean.Application.Common.Caching;
using System.Text.Json;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProducts;

namespace loxxking_backend_clean.Application.Features.BundleOffers.Queries.GetBundleOffers;

public class GetBundleOffersHandler : IRequestHandler<GetBundleOffersQuery, Result<List<BundleOfferResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public GetBundleOffersHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<List<BundleOfferResponse>>> Handle(GetBundleOffersQuery request, CancellationToken cancellationToken)
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var cacheKey = await CatalogCache.KeyAsync(_cache, $"BundleOffers_{lang}_{request.ActiveOnly}", cancellationToken);

        var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedBundles = JsonSerializer.Deserialize<List<BundleOfferResponse>>(cachedData);
            if (cachedBundles != null) return Result.Success(cachedBundles);
        }

        var query = _context.BundleOffers
            .Include(b => b.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Category)
            .AsQueryable();

        if (request.ActiveOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(b => b.ActivePeriod.StartDate <= now && b.ActivePeriod.EndDate >= now);
        }

        var bundles = await query
            .OrderByDescending(b => b.ActivePeriod.StartDate)
            .ToListAsync(cancellationToken);

        var responses = bundles.Select(b =>
        {
            var items = b.Items.Select(i => new BundleOfferItemResponse(
                new ProductListResponse(
                    i.Product.Id,
                    i.Product.Slug,
                    i.Product.NameEn,
                    i.Product.NameAr,
                    i.Product.Description,
                    i.Product.Description,
                    i.Product.BasePrice.Value,
                    i.Product.OriginalPrice != null ? i.Product.OriginalPrice.Value : null,
                    i.Product.Images,
                    i.Product.Category.NameEn,
                    i.Product.Sizes,
                    i.Product.SizeChartJson,
                    i.Product.Stock,
                    i.Product.Rating,
                    i.Product.ReviewCount,
                    i.Product.IsNew,
                    i.Product.IsBestSeller,
                    null, // Bundle items don't have individual offer badges
                    i.Product.Colors
                ),
                i.Quantity
            )).ToList();

            decimal originalPrice = items.Sum(i => i.Product.Price * i.Quantity);
            decimal saving = originalPrice - b.BundlePrice.Value;
            decimal discountPercent = originalPrice > 0 ? (saving / originalPrice) * 100 : 0;

            return new BundleOfferResponse(
                b.Id,
                b.Title,
                b.Subtitle,
                b.BundlePrice.Value,
                originalPrice,
                saving,
                Math.Round(discountPercent, 2),
                b.ImageUrl,
                b.ActivePeriod.StartDate,
                b.ActivePeriod.EndDate,
                items
            );
        }).ToList();

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(responses), options, cancellationToken);

        return Result.Success(responses);
    }
}
