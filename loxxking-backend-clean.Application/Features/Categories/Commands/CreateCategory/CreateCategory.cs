using loxxking_backend_clean.Domain.Entities.Categories;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Categories.Commands.CreateCategory;

public record CreateCategoryCommand(string NameEn, string NameAr) : IRequest<Result<Guid>>;

public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    public CreateCategoryHandler(IApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var slug = request.NameEn.ToLower().Replace(" ", "-") + "-" + Guid.NewGuid().ToString().Substring(0, 8);
        var cat = Category.Create(request.NameAr, request.NameEn, slug, string.Empty);
        _context.Categories.Add(cat);
        await _context.SaveChangesAsync(cancellationToken);

        // `GetCategories` caches the list for ten minutes and nothing ever cleared it, so a new
        // category stayed invisible for that long — including to the storefront's category pages,
        // which resolve a url slug through this list. Products filed under it could not be browsed
        // at all until the cache happened to expire.
        await _cache.RemoveAsync("CategoriesList", cancellationToken);

        return Result.Success(cat.Id);
    }
}
