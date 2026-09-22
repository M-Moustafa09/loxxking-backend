using loxxking_backend_clean.Domain.Entities.Offers;

namespace loxxking_backend_clean.Application.Features.BundleOffers.Commands.CreateBundleOffer;

public record BundleItemDto(Guid ProductId, int Quantity);

public record CreateBundleOfferCommand(
    string Title,
    string Subtitle,
    decimal BundlePrice,
    string ImageUrl,
    DateTime StartDate,
    DateTime EndDate,
    List<BundleItemDto> Items
) : IRequest<Result<Guid>>;

public class CreateBundleOfferHandler : IRequestHandler<CreateBundleOfferCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateBundleOfferHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> Handle(CreateBundleOfferCommand request, CancellationToken cancellationToken)
    {
        if (request.BundlePrice < 0) return Result.Failure<Guid>(new Error("InvalidData", "BundleOffer_PriceCannotBeNegative"));
        if (request.EndDate <= request.StartDate) return Result.Failure<Guid>(new Error("InvalidData", "Offer_EndDateMustBeAfterStartDate"));
        if (request.Items == null || request.Items.Count == 0) return Result.Failure<Guid>(new Error("InvalidData", "BundleOffer_MustContainItems"));

        var offer = BundleOffer.Create(
            request.Title,
            request.Subtitle,
            loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(request.BundlePrice),
            request.ImageUrl,
            loxxking_backend_clean.Domain.ValueObjects.DateRange.Create(request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime())
        );

        foreach (var item in request.Items)
        {
            offer.AddItem(item.ProductId, item.Quantity);
        }

        _context.BundleOffers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(offer.Id);
    }
}
