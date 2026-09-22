using loxxking_backend_clean.Domain.Entities.Offers;

namespace loxxking_backend_clean.Application.Features.Offers.Commands.CreateOffer;

public class CreateOfferHandler : IRequestHandler<CreateOfferCommand, Result<CreateOfferResponse>>
{
    private readonly IApplicationDbContext _context;

    public CreateOfferHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreateOfferResponse>> Handle(CreateOfferCommand request, CancellationToken cancellationToken)
    {
        var productExists = await _context.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken);
        if (!productExists)
        {
            return Result.Failure<CreateOfferResponse>(new Error("Error.Validation", "Offer_InvalidProduct"));
        }

        // The screen sends the admin's local time as UTC ("Z"); the columns and every comparison are UTC.
        var discount = loxxking_backend_clean.Domain.ValueObjects.Percentage.FromDecimal(request.DiscountPercent);
        var period = loxxking_backend_clean.Domain.ValueObjects.DateRange.Create(request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime());

        var periodError = await OfferPeriodGuard.CheckAsync(_context, request.ProductId, period.StartDate, period.EndDate, null, cancellationToken);
        if (periodError is not null) return Result.Failure<CreateOfferResponse>(periodError);

        var offer = Offer.Create(request.ProductId, discount, period);

        _context.Offers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateOfferResponse(offer.Id));
    }
}
