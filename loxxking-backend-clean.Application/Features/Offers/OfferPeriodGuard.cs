namespace loxxking_backend_clean.Application.Features.Offers;

/// <summary>
/// One offer per product at a time (owner decision 2026-09-22): a new or edited offer may not share
/// a single moment with another offer on the same product.
/// </summary>
internal static class OfferPeriodGuard
{
    public static async Task<Error?> CheckAsync(
        IApplicationDbContext context, Guid productId, DateTime startUtc, DateTime endUtc, Guid? excludeOfferId,
        CancellationToken cancellationToken)
    {
        if (endUtc <= DateTime.UtcNow)
            return new Error("Error.Validation", "Offer_EndDateInPast");

        var overlaps = await context.Offers.AnyAsync(o =>
            o.ProductId == productId
            && o.Id != excludeOfferId
            && o.ActivePeriod.StartDate <= endUtc
            && o.ActivePeriod.EndDate >= startUtc, cancellationToken);

        return overlaps ? new Error("Error.Validation", "Offer_PeriodOverlaps") : null;
    }
}
