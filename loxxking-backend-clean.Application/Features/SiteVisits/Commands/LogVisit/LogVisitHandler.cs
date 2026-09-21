using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Domain.Entities.Notifications;
using loxxking_backend_clean.Domain.Entities.SiteVisits;
using loxxking_backend_clean.Domain.Entities.Countries;

namespace loxxking_backend_clean.Application.Features.SiteVisits.Commands.LogVisit;

public class LogVisitHandler : IRequestHandler<LogVisitCommand, Result<LogVisitResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IGeolocationService? _geolocationService;
    private readonly IIpResolverService? _ipResolver;
    private readonly IStringLocalizer<SharedResource>? _localizer;
    private readonly IStoreVisitForwarder? _crmForwarder;

    public LogVisitHandler(
        IApplicationDbContext context,
        IGeolocationService? geolocationService = null,
        IIpResolverService? ipResolver = null,
        IStringLocalizer<SharedResource>? localizer = null,
        IStoreVisitForwarder? crmForwarder = null)
    {
        _context = context;
        _geolocationService = geolocationService;
        _ipResolver = ipResolver;
        _localizer = localizer;
        _crmForwarder = crmForwarder;
    }

    public async Task<Result<LogVisitResponse>> Handle(LogVisitCommand request, CancellationToken cancellationToken)
    {
        Country? country = null;
        // The country the CRM is told about. Unlike `country`, it never falls back to the store's
        // default country: an unresolved IP is reported as unknown rather than as Egypt.
        string? visitorCountryName = null;

        if (request.CountryId.HasValue)
        {
            if (request.CountryId.Value == Guid.Empty)
            {
                return Result.Failure<LogVisitResponse>(new Error("Error.NotFound", "Country_NotFound"));
            }

            country = await _context.Countries.FirstOrDefaultAsync(c => c.Id == request.CountryId.Value, cancellationToken);
            if (country == null)
            {
                return Result.Failure<LogVisitResponse>(new Error("Error.NotFound", "Country_NotFound"));
            }

            visitorCountryName = country.Name;
        }
        else
        {
            // If country not provided, try to resolve dynamically from IP if public
            if (!string.IsNullOrWhiteSpace(request.IpAddress) && _ipResolver != null && _ipResolver.IsValidPublicIp(request.IpAddress) && _geolocationService != null)
            {
                try
                {
                    var geo = await _geolocationService.GetGeoLocationAsync(request.IpAddress, cancellationToken);
                    if (geo != null && !string.IsNullOrEmpty(geo.CountryCode))
                    {
                        visitorCountryName = string.IsNullOrWhiteSpace(geo.CountryName) ? geo.CountryCode : geo.CountryName;

                        // Only a country the store sells in. A visit no longer creates a country
                        // (owner decision 2026-09-21: the list is fixed); others fall back below.
                        var code = geo.CountryCode.Trim().ToUpper();
                        country = await _context.Countries
                            .FirstOrDefaultAsync(c => c.Code == code && c.IsActive && !c.IsDeleted, cancellationToken);
                    }
                }
                catch
                {
                    // Ignore geo exceptions
                }
            }

            // Fallback to default country in DB if still not resolved
            if (country == null)
            {
                country = await _context.Countries
                    .OrderByDescending(c => c.IsDefault)
                    .ThenBy(c => c.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        var countryId = country?.Id;

        var siteVisit = SiteVisit.Create(countryId, request.Page, request.IpAddress);

        _context.SiteVisits.Add(siteVisit);

        if (request.Page.ToLower().Contains("checkout"))
        {
            var admins = await _context.Users
                .Where(u => u.Role == UserRole.Admin)
                .ToListAsync(cancellationToken);

            var countryName = country?.Name ?? "Unknown Country";
            var notifMsg = _localizer?.Get("Notification_SiteVisitAlert", $"New visit from {countryName} on {request.Page}", countryName, request.Page) ?? $"New visit from {countryName} on {request.Page}";
            foreach (var admin in admins)
            {
                _context.Notifications.Add(Notification.Create(
                    admin.Id,
                    NotificationType.SystemAlert,
                    notifMsg,
                    siteVisit.Id
                ));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (request.ForwardToCrm)
        {
            _crmForwarder?.Enqueue(new StoreVisitNotice(
                visitorCountryName,
                request.Page,
                request.Language,
                request.IsNewVisitor,
                DateTime.UtcNow));
        }

        var successMsg = _localizer.Get("SiteVisit_TrackedSuccessfully", "Site visit tracked successfully.");
        return Result.Success(new LogVisitResponse(siteVisit.Id, successMsg));
    }
}
