namespace loxxking_backend_clean.Application.Features.Orders.Commands.CreateOrder;

public record CreateOrderCommand(
    string Address,
    string Phone,
    string? Notes,
    PaymentMethod PaymentMethod,
    List<OrderItemDto> Items,
    Guid? CountryId,
    string? GuestName,
    string? GuestCountryName
) : IRequest<Result<CreateOrderResponse>>;

public record OrderItemDto(Guid ProductId, int Quantity);

public record CreateOrderResponse(Guid OrderId, string OrderNumber, decimal TotalAmount, Guid CountryId, string Currency = "");
