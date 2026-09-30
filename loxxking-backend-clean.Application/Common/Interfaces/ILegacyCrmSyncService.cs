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
    // ISO currency of TotalAmount and every UnitPrice: the order country's (per-country pricing).
    public string Currency { get; set; } = string.Empty;
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

    /// <summary>
    /// Hands the customer's bank-transfer receipt to the CRM, which attaches it to the order already synced
    /// from <paramref name="loxxkingOrderId"/>. Error code <c>ReceiptRejected</c>: the CRM will not take this
    /// file however often it is sent (not an image, too large).
    /// </summary>
    Task<Result> SendBankTransferReceiptAsync(Guid loxxkingOrderId, Stream receipt, string fileName, string contentType, CancellationToken cancellationToken = default);
}
