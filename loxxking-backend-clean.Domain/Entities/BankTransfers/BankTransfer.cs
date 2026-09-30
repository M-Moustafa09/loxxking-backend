using loxxking_backend_clean.Domain.Entities.Orders;

namespace loxxking_backend_clean.Domain.Entities.BankTransfers;
public class BankTransfer : BaseEntity {
    public Guid OrderId { get; private set; }
    public Order Order { get; private set; } = null!;
    public string ProofImageUrl { get; private set; } = string.Empty;
    public BankTransferStatus Status { get; private set; } = BankTransferStatus.PendingReview;
    public DateTime SubmittedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    // The receipt follows its order to the Luxira CRM, where staff review the transfer (OrderSyncBackgroundService).
    /// <summary>When the receipt was attached to the CRM order; null until then.</summary>
    public DateTime? CrmReceiptSentAt { get; private set; }
    /// <summary>Failed attempts to send the receipt to the CRM.</summary>
    public int CrmReceiptAttempts { get; private set; }
    /// <summary>After a failed attempt, the earliest time to try again (null: due now).</summary>
    public DateTime? CrmReceiptNextAttemptAt { get; private set; }
    /// <summary>Why the last attempt failed, for whoever looks into a receipt the CRM never got.</summary>
    public string? CrmReceiptLastError { get; private set; }

    protected BankTransfer() { }

    public static BankTransfer Create(Guid orderId, string proofImageUrl)
    {
        return new BankTransfer
        {
            OrderId = orderId,
            ProofImageUrl = proofImageUrl,
            Status = BankTransferStatus.PendingReview,
            SubmittedAt = DateTime.UtcNow
        };
    }

    public void Approve()
    {
        Status = BankTransferStatus.Approved;
        ReviewedAt = DateTime.UtcNow;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Domain_BankTransfer_RejectionReasonRequired", nameof(reason));
        }

        Status = BankTransferStatus.Rejected;
        ReviewedAt = DateTime.UtcNow;
        RejectionReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCrmReceiptSent(DateTime sentAt)
    {
        CrmReceiptSentAt = sentAt;
        CrmReceiptNextAttemptAt = null;
        CrmReceiptLastError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A send to the CRM failed: counted, and not tried again before <paramref name="retryAt"/>.</summary>
    public void RecordCrmReceiptFailure(string? error, DateTime retryAt)
    {
        CrmReceiptAttempts++;
        CrmReceiptNextAttemptAt = retryAt;
        CrmReceiptLastError = error is { Length: > 1000 } ? error[..1000] : error;
        UpdatedAt = DateTime.UtcNow;
    }
}
