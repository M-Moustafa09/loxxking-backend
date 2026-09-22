using loxxking_backend_clean.Domain.Entities.Products;

namespace loxxking_backend_clean.Application.Features.ProductPrices.Commands.SetProductPrice;

public class SetProductPriceHandler : IRequestHandler<SetProductPriceCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetProductPriceHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetProductPriceCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product is null) return Result.Failure(new Error("Error.NotFound", "Product_NotFound"));

        var country = await _context.Countries.FirstOrDefaultAsync(c => c.Id == request.CountryId, cancellationToken);
        if (country is null) return Result.Failure(new Error("Error.NotFound", "Country_NotFound"));

        var price = await _context.ProductPrices
            .FirstOrDefaultAsync(p => p.ProductId == request.ProductId && p.CountryId == request.CountryId, cancellationToken);

        if (price is null)
        {
            price = new ProductPrice { ProductId = request.ProductId, CountryId = request.CountryId, Price = request.Price };
            _context.ProductPrices.Add(price);
        }
        else
        {
            price.Price = request.Price;
            _context.ProductPrices.Update(price);
        }

        await _context.SaveChangesAsync(cancellationToken);
        
        return Result.Success();
    }
}
