using loxxking_backend_clean.Domain.Entities.Categories;
using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Categories.Commands.CreateCategory;

/// <summary>`Image` is optional: a data URL from the dashboard, or an existing URL.</summary>
public record CreateCategoryCommand(string NameEn, string NameAr, string? Image = null) : IRequest<Result<Guid>>;

public class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IFileStorageService _fileStorage;
    public CreateCategoryHandler(IApplicationDbContext context, IDistributedCache cache, IFileStorageService fileStorage)
    {
        _context = context;
        _cache = cache;
        _fileStorage = fileStorage;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var slug = await CategoryFields.UniqueSlugAsync(_context, request.NameEn, cancellationToken);
        var image = await CategoryFields.StoreImageAsync(_fileStorage, request.Image, cancellationToken);
        var cat = Category.Create(request.NameAr, request.NameEn, slug, image);
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
