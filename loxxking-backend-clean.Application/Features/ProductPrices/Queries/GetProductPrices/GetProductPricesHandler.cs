using Microsoft.Extensions.Caching.Distributed;
using loxxking_backend_clean.Application.Common.Caching;
using System.Text.Json;

namespace loxxking_backend_clean.Application.Features.ProductPrices.Queries.GetProductPrices;

public class GetProductPricesHandler : IRequestHandler<GetProductPricesQuery, Result<List<GetProductPricesResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public GetProductPricesHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<List<GetProductPricesResponse>>> Handle(GetProductPricesQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = await CatalogCache.KeyAsync(_cache, $"ProductPrices_{request.ProductId}", cancellationToken);
        var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
        
        List<GetProductPricesResponse>? prices = null;
        if (!string.IsNullOrEmpty(cachedData))
        {
            prices = JsonSerializer.Deserialize<List<GetProductPricesResponse>>(cachedData);
        }

        if (prices == null)
        {
            prices = await _context.ProductPrices
                .Where(p => p.ProductId == request.ProductId)
                .Select(p => new GetProductPricesResponse(p.CountryId, p.Country.Name, p.Price))
                .ToListAsync(cancellationToken);
            
            var options = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(prices), options, cancellationToken);
        }

        return Result.Success(prices);
    }
}
