using loxxking_backend_clean.Application.Features.Offers.Commands.CreateOffer;
using loxxking_backend_clean.Application.Features.Offers.Commands.DeleteOffer;
using loxxking_backend_clean.Application.Features.Offers.Commands.UpdateOffer;
using loxxking_backend_clean.Application.Features.Offers.Queries.GetOfferById;
using loxxking_backend_clean.Application.Features.Offers.Queries.GetManagedOffers;
using loxxking_backend_clean.Application.Features.Offers.Queries.GetOffers;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("api/offers")]
public class OffersController : ControllerBase
{
    private readonly ISender _sender;
    public OffersController(ISender sender) { _sender = sender; }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly, CancellationToken ct) => (await _sender.Send(new GetOffersQuery(activeOnly), ct)).ToApiResponse();

    // The dashboard's «إدارة العروض» list: every offer with its own id, running or not.
    [HttpGet("manage")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> GetManaged(CancellationToken ct) => (await _sender.Send(new GetManagedOffersQuery(), ct)).ToApiResponse();

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) => (await _sender.Send(new GetOfferByIdQuery(id), ct)).ToApiResponse();

    [HttpPost]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Create([FromBody] CreateOfferCommand cmd, CancellationToken ct) => (await _sender.Send(cmd, ct)).ToApiResponse();

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOfferCommand cmd, CancellationToken ct) => (await _sender.Send(cmd with { Id = id }, ct)).ToApiResponse();

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,StoreManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => (await _sender.Send(new DeleteOfferCommand(id), ct)).ToApiResponse();

}
