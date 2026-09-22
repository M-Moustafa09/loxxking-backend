using loxxking_backend_clean.Domain.Entities.Offers;
using loxxking_backend_clean.Application.Features.BundleOffers.Commands.CreateBundleOffer;

namespace loxxking_backend_clean.Application.Features.BundleOffers.Commands.UpdateBundleOffer;

public record UpdateBundleOfferCommand(
    Guid Id,
    string Title,
    string Subtitle,
    decimal BundlePrice,
    string ImageUrl,
    DateTime StartDate,
    DateTime EndDate,
    List<BundleItemDto> Items
) : IRequest<Result>;

public class UpdateBundleOfferHandler : IRequestHandler<UpdateBundleOfferCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public UpdateBundleOfferHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(UpdateBundleOfferCommand request, CancellationToken cancellationToken)
    {
        if (request.BundlePrice < 0) return Result.Failure(new Error("InvalidData", "BundleOffer_PriceCannotBeNegative"));
        if (request.EndDate <= request.StartDate) return Result.Failure(new Error("InvalidData", "Offer_EndDateMustBeAfterStartDate"));
        if (request.Items == null || request.Items.Count == 0) return Result.Failure(new Error("InvalidData", "BundleOffer_MustContainItems"));

        var offer = await _context.BundleOffers
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (offer == null) return Result.Failure(new Error("Error.NotFound", "BundleOffer_NotFound"));

        offer.UpdateDetails(
            request.Title,
            request.Subtitle,
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(request.BundlePrice),
            request.ImageUrl,
            loxxking_backend_clean.Domain.ValueObjects.DateRange.Create(request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime())
        );

        _context.BundleOfferItems.RemoveRange(offer.Items);
        offer.ClearItems();
        foreach (var item in request.Items)
        {
            offer.AddItem(item.ProductId, item.Quantity);
        }

        _context.BundleOffers.Update(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
