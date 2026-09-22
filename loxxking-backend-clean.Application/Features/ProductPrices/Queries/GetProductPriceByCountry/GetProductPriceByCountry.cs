using Microsoft.Extensions.Caching.Distributed;
using loxxking_backend_clean.Application.Common.Caching;
using System.Text.Json;

namespace loxxking_backend_clean.Application.Features.ProductPrices.Queries.GetProductPriceByCountry;

public record GetProductPriceByCountryQuery(Guid ProductId, Guid CountryId) : IRequest<Result<object>>;

public class GetProductPriceByCountryHandler : IRequestHandler<GetProductPriceByCountryQuery, Result<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public GetProductPriceByCountryHandler(IApplicationDbContext context, IDistributedCache cache) 
    { 
        _context = context; 
        _cache = cache;
    }

    public async Task<Result<object>> Handle(GetProductPriceByCountryQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = await CatalogCache.KeyAsync(_cache, $"ProductPrice_Country_{request.ProductId}_{request.CountryId}", cancellationToken);

        var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedPrice = JsonSerializer.Deserialize<object>(cachedData);
            if (cachedPrice != null) return Result.Success(cachedPrice);
        }

        var pp = await _context.ProductPrices.Where(p => p.ProductId == request.ProductId && p.CountryId == request.CountryId)
            .Select(p => new { p.ProductId, p.CountryId, p.Price, CountryName = p.Country.Name, p.Country.Currency })
            .FirstOrDefaultAsync(cancellationToken);
        if (pp == null) return Result.Failure<object>(new Error("Error.NotFound", "Product_PriceNotFound"));

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(pp), options, cancellationToken);

        return Result.Success<object>(pp);
    }
}
