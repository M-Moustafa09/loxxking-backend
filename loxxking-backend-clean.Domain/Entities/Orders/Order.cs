using loxxking_backend_clean.Domain.Entities.Users;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.ValueObjects;
namespace loxxking_backend_clean.Domain.Entities.Orders;
public class Order : BaseEntity {
    public string OrderNumber { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public User? User { get; private set; }
    public Guid? CustomerId { get; private set; }
    public User? Customer { get; private set; }
    public Guid CountryId { get; private set; }
    public Country Country { get; private set; } = null!;
    public OrderStatus Status { get; private set; } = OrderStatus.NewOrder;
    public PaymentMethod PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;
    public string? ShipmentCode { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? GuestName { get; private set; }
    public string? GuestPhone { get; private set; }
    public string? GuestAddress { get; private set; }
    public Money TotalAmount { get; private set; } = Money.Zero;

    /// <summary>
    /// ISO currency of every amount on this order (lines and total): the order country's currency,
    /// since each line is priced at that country's price (per-country pricing, 2026-09-21).
    /// </summary>
    public string Currency { get; private set; } = string.Empty;
    
    public string City { get; private set; } = string.Empty;
    public string Area { get; private set; } = string.Empty;
    public string DeliveryCompany { get; private set; } = string.Empty;
    public string? EstimatedDelivery { get; private set; }
    public string? Gender { get; private set; }
    public Money Subtotal { get; private set; } = Money.Zero;
    public Money Shipping { get; private set; } = Money.Zero;
    public Money Discount { get; private set; } = Money.Zero;
    public string? BankTransferReceiptUrl { get; private set; }
    public int ViewCount { get; private set; }
    public int ProcessedCount { get; private set; }
    public bool IsSynced { get; private set; } = false;

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    protected Order() { }

    public static Order Create(
        Guid? customerId,
        Guid countryId,
        string orderNumber,
        string address,
        string phone,
        string? notes,
        PaymentMethod paymentMethod,
        string? guestName = null,
        string? guestPhone = null,
        string? guestAddress = null,
        string currency = "",
        string? city = null,
        string? area = null)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CountryId = countryId,
            OrderNumber = orderNumber,
            Address = address,
            Phone = phone,
            Notes = notes,
            PaymentMethod = paymentMethod,
            Status = OrderStatus.NewOrder,
            PaymentStatus = paymentMethod == PaymentMethod.BankTransfer ? PaymentStatus.PendingVerification : PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            GuestName = guestName,
            GuestPhone = guestPhone,
            GuestAddress = guestAddress,
            Currency = currency,
            City = (city ?? string.Empty).Trim(),
            Area = (area ?? string.Empty).Trim(),
            IsSynced = false
        };
    }

    public void AddItem(Guid productId, int quantity, Money priceAtOrder)
    {
        _orderItems.Add(new OrderItem {
            ProductId = productId,
            Quantity = quantity,
            PriceAtOrder = priceAtOrder.Value
        });
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        decimal total = _orderItems.Sum(i => i.Quantity * i.PriceAtOrder);
        TotalAmount = Money.FromDecimal(total);
    }

    public void ChangeStatus(OrderStatus newStatus)
    {
        Status = newStatus;
    }

    public void ChangePaymentStatus(PaymentStatus newStatus)
    {
        PaymentStatus = newStatus;
    }

    public void SetBankTransferReceiptUrl(string url)
    {
        BankTransferReceiptUrl = url;
    }

    public void UpdateDetails(string phone, string address, string? shipmentCode)
    {
        Phone = phone;
        Address = address;
        ShipmentCode = shipmentCode;
    }

    public void MarkAsSynced()
    {
        IsSynced = true;
        UpdatedAt = DateTime.UtcNow;
    }
}

