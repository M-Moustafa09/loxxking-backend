namespace loxxking_backend_clean.Application.Features.SiteVisits.Commands.LogVisit;

/// <param name="Language">Storefront language the visitor sees ("ar" / "en"); forwarded to the CRM only.</param>
/// <param name="IsNewVisitor">First visit from this browser; forwarded to the CRM only.</param>
/// <param name="ForwardToCrm">False for crawlers, so the CRM is notified about people only.</param>
public record LogVisitCommand(
    Guid? CountryId,
    string Page,
    string? IpAddress = null,
    string? Language = null,
    bool IsNewVisitor = false,
    bool ForwardToCrm = false) : IRequest<Result<LogVisitResponse>>;

public record LogVisitResponse(Guid Id, string Message);
