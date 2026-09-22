using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Application.Features.ProductPrices.Commands.UpsertProductPrice;

public record UpsertProductPriceCommand(Guid ProductId, Guid CountryId, decimal Price) : IRequest<Result>;

public class UpsertProductPriceHandler : IRequestHandler<UpsertProductPriceCommand, Result>
{
    private readonly IApplicationDbContext _context;
    
    public UpsertProductPriceHandler(IApplicationDbContext context) 
    { 
        _context = context; 
    }

    public async Task<Result> Handle(UpsertProductPriceCommand request, CancellationToken cancellationToken)
    {
        var pp = await _context.ProductPrices.FirstOrDefaultAsync(p => p.ProductId == request.ProductId && p.CountryId == request.CountryId, cancellationToken);
        if (pp == null) {
            var newPrice = new ProductPrice { ProductId = request.ProductId, CountryId = request.CountryId, Price = request.Price };
            _context.ProductPrices.Add(newPrice);
        } else {
            pp.Price = request.Price;
            _context.ProductPrices.Update(pp);
        }
        await _context.SaveChangesAsync(cancellationToken);
        
        return Result.Success();
    }
}
