namespace loxxking_backend_clean.Application.Features.Offers.Queries.GetManagedOffers;

/// <summary>
/// Every product offer — running, upcoming and ended — for the dashboard's «إدارة العروض» screen.
/// The public GET api/offers answers with product cards and so loses the offer's own id, which the
/// screen needs to edit or delete it.
/// </summary>
public record GetManagedOffersQuery : IRequest<Result<List<ManagedOfferResponse>>>;

public record ManagedOfferResponse(
    Guid Id,
    Guid ProductId,
    string ProductNameAr,
    string ProductNameEn,
    string? ProductImage,
    decimal DiscountPercent,
    DateTime StartDate,
    DateTime EndDate);

public class GetManagedOffersHandler : IRequestHandler<GetManagedOffersQuery, Result<List<ManagedOfferResponse>>>
{
    private readonly IApplicationDbContext _context;
    public GetManagedOffersHandler(IApplicationDbContext context) { _context = context; }

    public async Task<Result<List<ManagedOfferResponse>>> Handle(GetManagedOffersQuery request, CancellationToken cancellationToken)
    {
        var rows = await _context.Offers
            .AsNoTracking()
            .Where(o => !o.Product.IsDeleted)
            .OrderByDescending(o => o.ActivePeriod.StartDate)
            .Select(o => new
            {
                o.Id,
                o.ProductId,
                o.Product.NameAr,
                o.Product.NameEn,
                o.Product.Images,
                Percent = o.Discount.Value,
                o.ActivePeriod.StartDate,
                o.ActivePeriod.EndDate
            })
            .ToListAsync(cancellationToken);

        // The columns come back without a kind; they are UTC, and the screen shows them in the
        // admin's own time, so say so (a "Z" on the wire).
        return Result.Success(rows.Select(o => new ManagedOfferResponse(
            o.Id,
            o.ProductId,
            o.NameAr,
            o.NameEn,
            o.Images.FirstOrDefault(),
            o.Percent,
            DateTime.SpecifyKind(o.StartDate, DateTimeKind.Utc),
            DateTime.SpecifyKind(o.EndDate, DateTimeKind.Utc))).ToList());
    }
}
