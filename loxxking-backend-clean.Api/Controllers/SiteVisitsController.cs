using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.SiteVisits.Commands.LogVisit;
using loxxking_backend_clean.Application.Features.SiteVisits.Queries.GetTodayCount;
using loxxking_backend_clean.Application.Features.SiteVisits.Queries.GetVisits;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/site-visits")]
public class SiteVisitsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IIpResolverService _ipResolver;

    public SiteVisitsController(ISender sender, IIpResolverService ipResolver)
    {
        _sender = sender;
        _ipResolver = ipResolver;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> LogVisit([FromBody] LogVisitRequest request, CancellationToken cancellationToken)
    {
        var ip = _ipResolver.GetClientIpAddress();
        var language = request.Language is "ar" or "en" ? request.Language : null;
        var command = new LogVisitCommand(
            request.CountryId,
            request.Page,
            ip,
            language,
            request.IsNewVisitor,
            ForwardToCrm: !IsCrawler(Request.Headers.UserAgent.ToString()));
        var result = await _sender.Send(command, cancellationToken);
        return result.ToApiResponse();
    }

    [HttpGet("today-count")]
    [Authorize(Roles = "Admin,StoreManager,SalesEmployee")]
    public async Task<IActionResult> GetTodayCount(CancellationToken cancellationToken)
    {
        var query = new GetTodayCountQuery();
        var result = await _sender.Send(query, cancellationToken);
        return result.ToApiResponse();
    }

    [HttpGet]
    [Authorize(Roles = "Admin,StoreManager,SalesEmployee")]
    public async Task<IActionResult> GetVisits(
        [FromQuery] Guid? countryId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetVisitsQuery(countryId, dateFrom, dateTo, page, pageSize);
        var result = await _sender.Send(query, cancellationToken);
        return result.ToApiResponse();
    }

    // Search engines render the storefront's JavaScript too, so they reach this endpoint. Their
    // visits are still logged, but the CRM (popup + email per visit) is only told about people.
    private static readonly string[] CrawlerMarkers =
    {
        "bot", "crawler", "spider", "slurp", "headless", "lighthouse", "pagespeed", "preview", "facebookexternalhit"
    };

    private static bool IsCrawler(string userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
        || CrawlerMarkers.Any(marker => userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Language">Storefront language shown to the visitor ("ar" / "en").</param>
/// <param name="IsNewVisitor">First visit from this browser (set by the storefront).</param>
public record LogVisitRequest(Guid? CountryId, string Page, string? Language = null, bool IsNewVisitor = false);
