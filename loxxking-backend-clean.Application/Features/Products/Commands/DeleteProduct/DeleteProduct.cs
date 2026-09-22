namespace loxxking_backend_clean.Application.Features.Products.Commands.DeleteProduct;

public record DeleteProductCommand(Guid Id) : IRequest<Result>;

public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteProductHandler(IApplicationDbContext context) 
    { 
        _context = context; 
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
        if (product == null) return Result.Failure(new Error("Error.NotFound", "Product_NotFound"));
        // Soft delete: a hard delete is refused by the FKs from order lines, inventory, prices and
        // reviews, and would erase what past orders were for. The query filter hides it everywhere.
        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
