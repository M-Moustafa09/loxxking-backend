namespace loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder;

public record CreateOrderCommand(
    string Address,
    string Phone,
    string? Notes,
    PaymentMethod PaymentMethod,
    List<OrderItemDto> Items,
    Guid? CountryId,
    string? GuestName,
    string? GuestCountryName,
    // Typed freely at checkout (owner decision 2026-09-22). Optional here so a cached older
    // storefront that never sent them keeps working; the checkout page itself requires the city.
    string? City = null,
    string? Area = null
) : IRequest<Result<CreateOrderResponse>>;

public record OrderItemDto(Guid ProductId, int Quantity);

public record CreateOrderResponse(Guid OrderId, string OrderNumber, decimal TotalAmount, Guid CountryId, string Currency = "");
