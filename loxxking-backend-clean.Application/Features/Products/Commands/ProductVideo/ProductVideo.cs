namespace loxxking_backend_clean.Application.Features.Products.Commands.ProductVideo;

/// <summary>One video per product: an upload replaces the old one, and the old file is removed.</summary>
public record UploadProductVideoCommand(Guid ProductId, Stream FileStream, string FileName, string ContentType, long Length) : IRequest<Result<string>>;

public record DeleteProductVideoCommand(Guid ProductId) : IRequest<Result>;

public static class ProductVideoRules
{
    /// <summary>30 MB — kept under the server's request limit (web.config), and still quick to load on a phone.</summary>
    public const long MaxBytes = 30L * 1024 * 1024;

    /// <summary>Formats every browser plays in a plain &lt;video&gt; tag. (.mov is Safari-only.)</summary>
    public static readonly string[] Extensions = { ".mp4", ".webm" };
}

public class UploadProductVideoHandler : IRequestHandler<UploadProductVideoCommand, Result<string>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;

    public UploadProductVideoHandler(IApplicationDbContext context, IFileStorageService fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;
    }

    public async Task<Result<string>> Handle(UploadProductVideoCommand request, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(request.FileName ?? "").ToLowerInvariant();
        if (!ProductVideoRules.Extensions.Contains(ext))
            return Result.Failure<string>(new Error("Error.Validation", "Product_VideoFormat"));
        if (request.Length <= 0 || request.Length > ProductVideoRules.MaxBytes)
            return Result.Failure<string>(new Error("Error.Validation", "Product_VideoTooLarge"));

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product == null) return Result.Failure<string>(new Error("Error.NotFound", "Product_NotFound"));

        var oldUrl = product.VideoUrl;
        var url = await _fileStorage.UploadAsync(request.FileStream, $"video{ext}", request.ContentType, "products/videos", cancellationToken);

        product.SetVideoUrl(url);
        await _context.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(oldUrl))
            await _fileStorage.DeleteAsync(oldUrl, cancellationToken);

        return Result.Success(url);
    }
}

public class DeleteProductVideoHandler : IRequestHandler<DeleteProductVideoCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;

    public DeleteProductVideoHandler(IApplicationDbContext context, IFileStorageService fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(DeleteProductVideoCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product == null) return Result.Failure(new Error("Error.NotFound", "Product_NotFound"));

        var oldUrl = product.VideoUrl;
        if (string.IsNullOrEmpty(oldUrl)) return Result.Success();

        product.SetVideoUrl(null);
        await _context.SaveChangesAsync(cancellationToken);

        await _fileStorage.DeleteAsync(oldUrl, cancellationToken);

        return Result.Success();
    }
}
