using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Products.Commands.DeleteProduct;

public record DeleteProductCommand(Guid Id) : IRequest<Result>;

public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public DeleteProductHandler(IApplicationDbContext context, IDistributedCache cache) 
    { 
        _context = context; 
        _cache = cache;
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (product == null) return Result.Failure(new Error("Error.NotFound", "Product_NotFound"));
        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);

        // The list cache was never cleared here, so a deleted product kept showing up for up to
        // ten minutes — visible now that the dashboard deletes from a list it reloads right after.
        await _cache.RemoveAsync("ProductsList_v2_", cancellationToken);
        await _cache.RemoveAsync($"ProductsList_v2_{product.CategoryId}", cancellationToken);
        await _cache.RemoveAsync($"ProductDetail_{product.Id}_ar", cancellationToken);
        await _cache.RemoveAsync($"ProductDetail_{product.Id}_en", cancellationToken);

        return Result.Success();
    }
}
