using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/crm-menu")]
public class CrmMenuController : ControllerBase
{
    private const string CacheKey = "crm-sidebar-menu";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CrmMenuController> _logger;

    public CrmMenuController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<CrmMenuController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Hands the admin dashboard the CRM's own sidebar, so an admin who arrived from Luxira keeps
    /// the menu they know instead of this store's two-entry one, and can jump back to any CRM screen.
    ///
    /// The CRM is called server to server with the API key the order sync already uses — the admin's
    /// browser never talks to luxira.org, so there is no CORS and no cross-site cookie involved.
    ///
    /// Fails soft: if the CRM is unreachable or misconfigured the dashboard still loads, just without
    /// the CRM entries. A menu is not worth a broken admin page.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSidebar(CancellationToken ct)
    {
        // The links open the CRM in the browser, so they need its PUBLIC domain, not the
        // server-to-server BaseUrl (localhost in dev). This key already exists for the
        // shipment-tracking redirects.
        var crmPublicBaseUrl =
            (_configuration["LegacyCrm:PublicTrackingBaseUrl"] ?? string.Empty).TrimEnd('/');

        if (_cache.TryGetValue(CacheKey, out JsonElement cached))
        {
            return Ok(new { baseUrl = crmPublicBaseUrl, sections = cached });
        }

        var baseUrl = _configuration["LegacyCrm:BaseUrl"];
        var apiKey = _configuration["LegacyCrm:ApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("CRM menu unavailable: LegacyCrm:BaseUrl or LegacyCrm:ApiKey is not configured.");
            return Ok(new { baseUrl = crmPublicBaseUrl, sections = Array.Empty<object>() });
        }

        try
        {
            var client = _httpClientFactory.CreateClient("LegacyCrmClient");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl.TrimEnd('/')}/Api/StoreMenu/Sidebar");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {apiKey}");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "CRM menu request returned {StatusCode}; serving an empty menu.",
                    (int)response.StatusCode);
                return Ok(new { baseUrl = crmPublicBaseUrl, sections = Array.Empty<object>() });
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("sections", out var sections))
            {
                _logger.LogWarning("CRM menu response has no \"sections\" property; serving an empty menu.");
                return Ok(new { baseUrl = crmPublicBaseUrl, sections = Array.Empty<object>() });
            }

            // Clone: the JsonElement above dies with the JsonDocument, and the cache outlives it.
            var snapshot = sections.Clone();
            _cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(10));

            return Ok(new { baseUrl = crmPublicBaseUrl, sections = snapshot });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the CRM menu; serving an empty menu.");
            return Ok(new { baseUrl = crmPublicBaseUrl, sections = Array.Empty<object>() });
        }
    }
}
