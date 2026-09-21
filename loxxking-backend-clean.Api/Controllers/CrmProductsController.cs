using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Api.Controllers;

/// <summary>
/// The CRM's products and their codes, for the dashboard's product picker.
///
/// The code is how the CRM resolves the warehouse of an order line synced from this store, and the
/// dashboard used to take it as free text — a typo shipped orders with no warehouse. The admin now
/// picks a CRM product instead and the code comes with it.
///
/// Each entry says which live store product already carries its code, so the picker can hide codes
/// that are taken (one CRM product ↔ one store product). Called server to server with the same key
/// as the order sync and the CRM menu. Unlike the menu this does NOT fail soft: an empty picker would
/// look like "the CRM has no products", so a failure is reported as one.
/// </summary>
[ApiController]
[Route("api/crm-products")]
public class CrmProductsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CrmProductsController> _logger;

    public CrmProductsController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IApplicationDbContext context,
        ILogger<CrmProductsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _context = context;
        _logger = logger;
    }

    public record CrmProductItem(int Id, string Name, string ProductCode, string? Group, Guid? LinkedProductId);

    private sealed record CrmCatalogue(List<CrmCatalogueRow>? Products);
    private sealed record CrmCatalogueRow(int Id, string? Name, string? ProductCode, string? Group);

    [HttpGet]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        const string unavailable = "تعذر تحميل منتجات لوكسيرا، حاول مرة أخرى.";

        var baseUrl = _configuration["LegacyCrm:BaseUrl"];
        var apiKey = _configuration["LegacyCrm:ApiKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("CRM products unavailable: LegacyCrm:BaseUrl or LegacyCrm:ApiKey is not configured.");
            return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = unavailable });
        }

        List<CrmCatalogueRow> rows;
        try
        {
            var client = _httpClientFactory.CreateClient("LegacyCrmClient");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/Api/StoreProducts/Catalogue");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {apiKey}");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CRM products request returned {StatusCode}.", (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = unavailable });
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            var catalogue = JsonSerializer.Deserialize<CrmCatalogue>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            rows = catalogue?.Products ?? new List<CrmCatalogueRow>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the CRM products.");
            return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = unavailable });
        }

        // Live products only (the query filter hides deleted ones): a deleted product frees its code.
        var linked = await _context.Products
            .Where(p => p.ProductCode != null)
            .Select(p => new { p.Id, p.ProductCode })
            .ToListAsync(ct);
        var linkedByCode = linked
            .GroupBy(p => p.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var items = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ProductCode))
            .Select(r =>
            {
                var code = r.ProductCode!.Trim();
                return new CrmProductItem(
                    r.Id,
                    (r.Name ?? string.Empty).Trim(),
                    code,
                    r.Group,
                    linkedByCode.TryGetValue(code, out var productId) ? productId : null);
            })
            .ToList();

        return Ok(new { success = true, data = items });
    }
}
