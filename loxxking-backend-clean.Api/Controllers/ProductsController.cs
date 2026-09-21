using loxxking_backend_clean.Application.Features.Products.Commands.CreateProduct;
using loxxking_backend_clean.Application.Features.Products.Commands.DeleteProduct;
using loxxking_backend_clean.Application.Features.Products.Commands.DeleteProductImage;
using loxxking_backend_clean.Application.Features.Products.Commands.ProductVideo;
using loxxking_backend_clean.Application.Features.Products.Commands.UpdateProduct;
using loxxking_backend_clean.Application.Features.Products.Commands.UploadProductImage;
using loxxking_backend_clean.Application.Features.Products.Queries.GetBestSellers;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProduct;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProductDetail;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProducts;
using Microsoft.Extensions.Localization;
using loxxking_backend_clean.Shared;
using loxxking_backend_clean.Shared.Resources;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IStringLocalizer<SharedResource> _localizer;
    public ProductsController(ISender sender, IStringLocalizer<SharedResource> localizer) { _sender = sender; _localizer = localizer; }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? categoryId, CancellationToken ct) => (await _sender.Send(new GetProductsQuery(categoryId), ct)).ToApiResponse();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct) => (await _sender.Send(new GetProductQuery(id), ct)).ToApiResponse();

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken ct) => (await _sender.Send(new loxxking_backend_clean.Application.Features.Products.Queries.GetProductBySlug.GetProductBySlugQuery(slug), ct)).ToApiResponse();

    [HttpPost]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand cmd, CancellationToken ct) => (await _sender.Send(cmd, ct)).ToApiResponse();

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductCommand cmd, CancellationToken ct) => (await _sender.Send(cmd with { Id = id }, ct)).ToApiResponse();

    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct) {
        if(file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail(_localizer.Get("Product_ImageRequired", "Product image is required.")));
        var cmd = new UploadProductImageCommand(id, file.OpenReadStream(), file.FileName, file.ContentType);
        return (await _sender.Send(cmd, ct)).ToApiResponse();
    }

    [HttpDelete("{id:guid}/images")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> DeleteImage(Guid id, [FromQuery] string url, CancellationToken ct) => (await _sender.Send(new DeleteProductImageCommand(id, url), ct)).ToApiResponse();

    // A 30 MB video plus the multipart envelope needs more than Kestrel's ~28.6 MB default body limit.
    // IIS enforces its own limit too: see requestLimits in web.config.
    [HttpPost("{id:guid}/video")]
    [Authorize(Roles = "Admin,StoreManager")]
    [RequestSizeLimit(ProductVideoRules.MaxBytes + 2 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductVideoRules.MaxBytes + 2 * 1024 * 1024)]
    public async Task<IActionResult> UploadVideo(Guid id, IFormFile file, CancellationToken ct) {
        if(file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail(_localizer.Get("Product_VideoRequired", "Choose a video file.")));
        await using var stream = file.OpenReadStream();
        var cmd = new UploadProductVideoCommand(id, stream, file.FileName, file.ContentType, file.Length);
        return (await _sender.Send(cmd, ct)).ToApiResponse();
    }

    [HttpDelete("{id:guid}/video")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> DeleteVideo(Guid id, CancellationToken ct) => (await _sender.Send(new DeleteProductVideoCommand(id), ct)).ToApiResponse();

    [HttpGet("best-sellers")]
    public async Task<IActionResult> GetBestSellers(CancellationToken ct, [FromQuery] int top = 20) => (await _sender.Send(new GetBestSellersQuery(top), ct)).ToApiResponse();

    [HttpGet("{id:guid}/detail")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct) => (await _sender.Send(new GetProductDetailQuery(id), ct)).ToApiResponse();

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => (await _sender.Send(new DeleteProductCommand(id), ct)).ToApiResponse();

    [HttpPost("{id:guid}/reviews")]
    [Authorize]
    public async Task<IActionResult> SubmitReview(Guid id, [FromBody] SubmitReviewDto dto, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var cmd = new loxxking_backend_clean.Application.Features.Reviews.Commands.SubmitReview.SubmitReviewCommand(id, userId, dto.Rating, dto.Comment);
        return (await _sender.Send(cmd, ct)).ToApiResponse();
    }
}

public record SubmitReviewDto(int Rating, string Comment);
