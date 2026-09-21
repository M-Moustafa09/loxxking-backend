namespace loxxking_backend_clean.Application.Common.Interfaces;

/// <summary>
/// Hands a store visit to the Luxira CRM (owner request 2026-09-21: every visit → a CRM
/// notification + an email). Fire-and-forget: it only queues, so logging a visit never waits
/// on the CRM, and a CRM outage never breaks the storefront.
/// </summary>
public interface IStoreVisitForwarder
{
    void Enqueue(StoreVisitNotice visit);
}

/// <param name="Country">Country resolved from the visitor's IP, or null when unknown.</param>
/// <param name="Page">The path the visitor landed on, e.g. "/" or "/product/abc".</param>
/// <param name="Language">The storefront language shown to the visitor ("ar" / "en").</param>
/// <param name="IsNewVisitor">First visit from this browser.</param>
public record StoreVisitNotice(
    string? Country,
    string Page,
    string? Language,
    bool IsNewVisitor,
    DateTime VisitedAtUtc);
