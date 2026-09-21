using Microsoft.Extensions.Caching.Distributed;

namespace loxxking_backend_clean.Application.Features.Categories.Commands.UpdateCategory;

/// <summary>`Image` null keeps the current image, empty removes it. The slug never changes, so links stay valid.</summary>
public record UpdateCategoryCommand(Guid Id, string NameEn, string NameAr, string? Image = null) : IRequest<Result>;

public class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly IFileStorageService _fileStorage;
    public UpdateCategoryHandler(IApplicationDbContext context, IDistributedCache cache, IFileStorageService fileStorage)
    {
        _context = context;
        _cache = cache;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (cat == null) return Result.Failure(new Error("Error.NotFound", "Category_NotFound"));
        var image = request.Image == null
            ? cat.ImageUrl
            : await CategoryFields.StoreImageAsync(_fileStorage, request.Image, cancellationToken);
        cat.UpdateDetails(request.NameAr, request.NameEn, cat.Slug, image);
        _context.Categories.Update(cat);
        await _context.SaveChangesAsync(cancellationToken);

        // Nothing cleared the cached list, so a renamed category kept its old name for ten minutes.
        await _cache.RemoveAsync("CategoriesList", cancellationToken);

        return Result.Success();
    }
}
