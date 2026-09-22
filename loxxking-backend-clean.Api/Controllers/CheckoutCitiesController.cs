using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using loxxking_backend_clean.Api.Common;
using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Api.Controllers;

/// <summary>
/// City suggestions for the checkout page's free-text city field (owner decision 2026-09-22).
///
/// The list is the CRM's own city list for the order country. The city reaches the CRM order as
/// text and the CRM matches that text against each courier's cities when it ships, so a customer
/// who picks a suggestion ships without anyone retyping the city. The customer may still type any
/// city; suggestions only help.
///
/// Fails soft: no suggestions is a working checkout, so any failure answers an empty list (cached
/// briefly, so a CRM outage is not hit on every keystroke of every visitor).
/// </summary>
[ApiController]
[Route("api/checkout-cities")]
public class CheckoutCitiesController : ControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheFor = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CheckoutCitiesController> _logger;

    public CheckoutCitiesController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApplicationDbContext context,
        IMemoryCache cache,
        ILogger<CheckoutCitiesController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    private sealed record CrmCities(List<string>? Cities);

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get([FromQuery] Guid countryId, CancellationToken ct)
    {
        var cacheKey = $"checkout-cities:{countryId}";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
            return Ok(Answer(cached));

        // The store's own country row gives the name the CRM already understands from the order
        // sync ("Saudi Arabia"), so no free string from the browser is forwarded.
        var countryName = countryId == Guid.Empty
            ? null
            : await _context.Countries
                .Where(c => c.Id == countryId && c.IsActive && !c.IsDeleted)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(countryName))
            return Ok(Answer(new List<string>()));

        var cities = await FetchFromCrmAsync(countryName, ct);
        _cache.Set(cacheKey, cities ?? new List<string>(), cities is null ? FailureCacheFor : CacheFor);
        return Ok(Answer(cities ?? new List<string>()));
    }

    /// <summary>
    /// <c>data</c> stays the plain Arabic list a cached older storefront reads; <c>items</c> adds the
    /// English name for the storefront's language (null → show the Arabic). The order always carries
    /// the Arabic <c>name</c>.
    /// </summary>
    private static object Answer(List<string> cities) => new
    {
        success = true,
        data = cities,
        items = cities.Select(c => new { name = c, nameEn = CheckoutCityEnglishNames.For(c) }),
    };

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
