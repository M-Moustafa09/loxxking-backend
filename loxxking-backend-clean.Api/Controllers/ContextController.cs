using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/v1/context")]
public class ContextController : ControllerBase
{
    /// <summary>Currency of the international price, for visitors outside the store's countries.</summary>
    public const string InternationalCurrency = "USD";

    private readonly IIpResolverService _ipResolver;
    private readonly IGeolocationService _geolocationService;
    private readonly IApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public ContextController(
        IIpResolverService ipResolver,
        IGeolocationService geolocationService,
        IApplicationDbContext context,
        IWebHostEnvironment environment)
    {
        _ipResolver = ipResolver;
        _geolocationService = geolocationService;
        _context = context;
        _environment = environment;
    }

    /// <summary>
    /// The visitor's country and the currency their prices are shown in.
    /// - A country the store sells in (active, one of the CRM's 16): its id and currency.
    /// - Any other country: countryId null and USD, so the storefront shows international prices.
    ///   The country is no longer created here (it used to be, which filled the table with every
    ///   country a visitor or crawler came from, twice when two arrived together).
    /// - IP unknown (local, private, geolocation down): the store's default country, as before.
    /// In Development, ?country=LY simulates a visitor from that country.
    /// </summary>
    [HttpGet("init")]
    [AllowAnonymous]
    public async Task<IActionResult> InitContext([FromQuery] string? country, CancellationToken cancellationToken)
    {
        string? code = null;
        string? geoName = null;

        if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(country))
        {
            code = country.Trim().ToUpperInvariant();
            geoName = code;
        }
        else
        {
            try
            {
                var ip = _ipResolver.GetClientIpAddress();
                if (!string.IsNullOrEmpty(ip) && _ipResolver.IsValidPublicIp(ip))
                {
                    var geo = await _geolocationService.GetGeoLocationAsync(ip, cancellationToken);
                    if (geo != null && !string.IsNullOrEmpty(geo.CountryCode))
                    {
                        code = geo.CountryCode.Trim().ToUpperInvariant();
                        geoName = string.IsNullOrWhiteSpace(geo.CountryName) ? code : geo.CountryName;
                    }
                }
            }
            catch
            {
                // Geolocation is best effort; fall back to the default country below.
            }
        }

        if (code != null)
        {
            var sold = await _context.Countries
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == code && c.IsActive && !c.IsDeleted, cancellationToken);

            if (sold != null)
            {
                return Ok(new
                {
                    country = sold.Name,
                    countryCode = code,
                    countryId = (Guid?)sold.Id,
                    currency = sold.Currency
                });
            }

            return Ok(new
            {
                country = geoName,
                countryCode = code,
                countryId = (Guid?)null,
                currency = InternationalCurrency
            });
        }

        var storeDefault = await _context.Countries
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            country = storeDefault?.Name ?? "Unknown",
            countryCode = storeDefault?.Code,
            countryId = storeDefault?.Id,
            currency = storeDefault?.Currency ?? InternationalCurrency
        });
    }
}
