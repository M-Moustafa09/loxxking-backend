namespace loxxking_backend_clean.Application.Features.Offers.Commands.CreateOffer;

public class CreateOfferValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferValidator(IStringLocalizer<SharedResource> localizer)
    {
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate).WithMessage(localizer["Offer_EndDateMustBeAfterStartDate"]);

        RuleFor(x => x.DiscountPercent)
            .GreaterThan(0).LessThan(100).WithMessage(localizer["Offer_DiscountPercentRange"]);
    }
}
