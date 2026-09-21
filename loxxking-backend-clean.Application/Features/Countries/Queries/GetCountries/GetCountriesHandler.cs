using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace loxxking_backend_clean.Application.Features.Countries.Queries.GetCountries;

public class GetCountriesHandler : IRequestHandler<GetCountriesQuery, Result<List<GetCountriesResponse>>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public GetCountriesHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<List<GetCountriesResponse>>> Handle(GetCountriesQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = "Countries_All";
        var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cachedCountries = JsonSerializer.Deserialize<List<GetCountriesResponse>>(cachedData);
            if (cachedCountries != null) return Result.Success(cachedCountries);
        }

        // Only the countries the store sells in (the CRM's 16). Rows created in the past from
        // visitors' IPs are switched off and must not appear in the dashboard's price picker.
        var countries = await _context.Countries
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new GetCountriesResponse(
                c.Id,
                c.Name,
                c.Currency,
                c.DefaultLanguage,
                c.Code,
                c.NameAr
            ))
            .ToListAsync(cancellationToken);

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(countries), options, cancellationToken);

        return Result.Success(countries);
    }
}
