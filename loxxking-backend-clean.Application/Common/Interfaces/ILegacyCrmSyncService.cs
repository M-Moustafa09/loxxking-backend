using loxxking_backend_clean.Shared;

namespace loxxking_backend_clean.Application.Common.Interfaces;

public class CrmOrderSyncDto
{
    public Guid LoxxkingOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<CrmOrderItemSyncDto> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class CrmOrderItemSyncDto
{
    public string ProductName { get; set; } = string.Empty;
    // The Luxira/CRM product code, so the CRM can resolve this line to its warehouse row (G3.1).
    // May be null for products an admin has not yet coded; the CRM decides how to handle that.
    public string? ProductCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CrmSyncResponse
{
    public int LegacyCrmOrderId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ILegacyCrmSyncService
{
    Task<Result<CrmSyncResponse>> SyncOrderAsync(CrmOrderSyncDto dto, CancellationToken cancellationToken = default);
}
