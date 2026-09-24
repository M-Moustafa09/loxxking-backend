using System.Text.Json;
using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace loxxking_backend_clean.Api.Services;

/// <summary>
/// Reads the CRM's cities for a store country (<c>Api/StoreCities</c>) and caches them: 6 hours, or
/// 2 minutes after a failure so a CRM outage is not asked again on every keystroke or order.
/// </summary>
public class CheckoutCityDirectory : ICheckoutCityDirectory
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheFor = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CheckoutCityDirectory> _logger;

    public CheckoutCityDirectory(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApplicationDbContext context,
        IMemoryCache cache,
        ILogger<CheckoutCityDirectory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    private sealed record CrmCities(List<string>? Cities);

    // A failure is cached as this marker, so it is told apart from a country with no cities.
    private sealed record Unavailable;

    public async Task<IReadOnlyList<string>?> GetCitiesAsync(Guid countryId, CancellationToken cancellationToken)
    {
        var cacheKey = $"checkout-cities:{countryId}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached as List<string>;

        // The store's own country row gives the name the CRM already understands from the order
        // sync ("Saudi Arabia"), so no free string from the browser is forwarded.
        var countryName = countryId == Guid.Empty
            ? null
            : await _context.Countries
                .Where(c => c.Id == countryId && c.IsActive && !c.IsDeleted)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(countryName))
            return new List<string>();

        var cities = await FetchFromCrmAsync(countryName, cancellationToken);
        _cache.Set(cacheKey, (object?)cities ?? new Unavailable(), cities is null ? FailureCacheFor : CacheFor);
        return cities;
    }

    /// <summary>The CRM's cities for the country, or null when the CRM could not be read.</summary>
    private async Task<List<string>?> FetchFromCrmAsync(string countryName, CancellationToken ct)
    {
        var baseUrl = _configuration["LegacyCrm:BaseUrl"];
        var apiKey = _configuration["LegacyCrm:ApiKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Checkout cities unavailable: LegacyCrm:BaseUrl or LegacyCrm:ApiKey is not configured.");
            return null;
        }

        try
        {
            // A visitor is waiting on the page: give up well before the client's general timeout.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var client = _httpClientFactory.CreateClient("LegacyCrmClient");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl.TrimEnd('/')}/Api/StoreCities?country={Uri.EscapeDataString(countryName)}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {apiKey}");

            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CRM cities request for {Country} returned {StatusCode}.", countryName, (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            var body = JsonSerializer.Deserialize<CrmCities>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return body?.Cities ?? new List<string>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the CRM cities for {Country}.", countryName);
            return null;
        }
    }
}
