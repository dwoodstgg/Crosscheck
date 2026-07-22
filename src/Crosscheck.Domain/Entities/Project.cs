using Crosscheck.Domain.Enums;

namespace Crosscheck.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public required string Name { get; set; }

    /// <summary>Short code for invoices/reports, e.g. GEO-014 or INT-LEAVE. Unique per
    /// client (not globally) — the same code may recur across different clients.</summary>
    public required string Code { get; set; }

    /// <summary>Status changes are always explicit user actions — never automatic
    /// (budget exhaustion must not close a project).</summary>
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }
    public Employee? ClosedBy { get; set; }

    public Guid ProjectManagerId { get; set; }
    public Employee? ProjectManager { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>USD only in v1; column kept for the future.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>What this project calls its budget breakdown sections — "modules" or
    /// "milestones". Display only; mechanics are identical.</summary>
    public BreakdownLabel BreakdownLabel { get; set; } = BreakdownLabel.Module;

    // Billing overrides (design decision 18). Null = inherit the client's default; effective
    // value resolves per-field project → client → default. See ProjectBilling.Resolve.
    public string? BillingContactName { get; set; }
    public string? BillingContactEmail { get; set; }
    public BillingAddress? BillingAddress { get; set; }
    public int? PaymentTermsDays { get; set; }
}
