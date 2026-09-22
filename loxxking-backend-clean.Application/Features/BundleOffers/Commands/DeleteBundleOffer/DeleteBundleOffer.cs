namespace loxxking_backend_clean.Application.Features.BundleOffers.Commands.DeleteBundleOffer;

public record DeleteBundleOfferCommand(Guid Id) : IRequest<Result>;

public class DeleteBundleOfferHandler : IRequestHandler<DeleteBundleOfferCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteBundleOfferHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(DeleteBundleOfferCommand request, CancellationToken cancellationToken)
    {
        var offer = await _context.BundleOffers
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (offer == null) return Result.Failure(new Error("Error.NotFound", "BundleOffer_NotFound"));

        _context.BundleOffers.Remove(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
