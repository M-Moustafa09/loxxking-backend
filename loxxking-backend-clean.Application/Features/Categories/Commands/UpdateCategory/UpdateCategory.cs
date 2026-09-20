using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Categories.Commands.UpdateCategory;

public record UpdateCategoryCommand(Guid Id, string NameEn, string NameAr) : IRequest<Result>;

public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    public UpdateCategoryHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (cat == null) return Result.Failure(new Error("Error.NotFound", "Category_NotFound"));
        cat.UpdateDetails(request.NameAr, request.NameEn, cat.Slug, cat.ImageUrl);
        _context.Categories.Update(cat);
        await _context.SaveChangesAsync(cancellationToken);

        // Nothing cleared the cached list, so a renamed category kept its old name for ten minutes.
        await _cache.RemoveAsync("CategoriesList", cancellationToken);

        return Result.Success();
    }
}
