using Waypoint.Domain.Entities;

namespace Waypoint.Application.Projects;

/// <summary>Project-level billing overrides from the UI. A null field inherits the client's default.</summary>
public record ProjectBillingInput(
    string? ContactName,
    string? ContactEmail,
    BillingAddress? Address,
    int? PaymentTermsDays);

/// <summary>The effective billing profile after resolving project → client → default, with flags
/// indicating whether the project supplied the value (so the UI can show "from project" vs "from client").</summary>
public record BillingProfile(
    string? ContactName,
    string? ContactEmail,
    BillingAddress? Address,
    int PaymentTermsDays,
    bool ContactFromProject,
    bool TermsFromProject);

/// <summary>Resolves a project's effective billing (design decision 18): each field is the project's
/// value when set, otherwise the client's, otherwise a system default for payment terms.</summary>
public static class ProjectBilling
{
    public const int DefaultPaymentTermsDays = 30;

    public static BillingProfile Resolve(Project project, Client? client)
    {
        var contactFromProject = project.BillingContactName is not null
            || project.BillingContactEmail is not null
            || project.BillingAddress is not null;

        return new BillingProfile(
            ContactName: project.BillingContactName ?? client?.BillingContactName,
            ContactEmail: project.BillingContactEmail ?? client?.BillingContactEmail,
            Address: project.BillingAddress ?? client?.BillingAddress,
            PaymentTermsDays: project.PaymentTermsDays ?? client?.PaymentTermsDays ?? DefaultPaymentTermsDays,
            ContactFromProject: contactFromProject,
            TermsFromProject: project.PaymentTermsDays is not null);
    }
}
