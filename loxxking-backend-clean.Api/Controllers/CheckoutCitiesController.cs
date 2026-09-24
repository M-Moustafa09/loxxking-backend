using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.Orders.CheckoutCities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace loxxking_backend_clean.Api.Controllers;

/// <summary>
/// City suggestions for the checkout page's free-text city field (owner decision 2026-09-22).
///
/// The list is the CRM's own city list for the order country (<see cref="ICheckoutCityDirectory"/>).
/// The city reaches the CRM order as text and the CRM matches that text against each courier's
/// cities when it ships, so a customer who picks a suggestion ships without anyone retyping the city.
/// The customer may still type any city; the order then takes the CRM spelling of the city they
/// meant (<see cref="CheckoutCityMatcher"/>).
///
/// Fails soft: no suggestions is a working checkout, so any failure answers an empty list.
/// </summary>
[ApiController]
[Route("api/checkout-cities")]
public class CheckoutCitiesController : ControllerBase
{
    private readonly ICheckoutCityDirectory _cities;

    public CheckoutCitiesController(ICheckoutCityDirectory cities)
    {
        _cities = cities;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get([FromQuery] Guid countryId, CancellationToken ct)
    {
        var cities = await _cities.GetCitiesAsync(countryId, ct);
        return Ok(Answer(cities ?? Array.Empty<string>()));
    }

    /// <summary>
    /// <c>data</c> stays the plain Arabic list a cached older storefront reads; <c>items</c> adds the
    /// English name for the storefront's language (null → show the Arabic). The order always carries
    /// the Arabic <c>name</c>.
    /// </summary>
    private static object Answer(IReadOnlyList<string> cities) => new
    {
        success = true,
        data = cities,
        items = cities.Select(c => new { name = c, nameEn = CheckoutCityEnglishNames.For(c) }),
    };
}
