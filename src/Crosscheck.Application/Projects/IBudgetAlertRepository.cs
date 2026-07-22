using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Projects;

public interface IBudgetAlertRepository
{
    /// <summary>The alert keys already fired for this budget (so they aren't re-sent).</summary>
    Task<IReadOnlySet<string>> GetFiredKeysAsync(Guid budgetId, CancellationToken cancellationToken = default);

    /// <summary>Records a fired alert. Idempotent on (budget_id, alert_key).</summary>
    Task RecordAsync(BudgetAlert alert, CancellationToken cancellationToken = default);

    /// <summary>Clears every fired alert for a budget — re-arms thresholds after a budget change.</summary>
    Task ClearForBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default);

    /// <summary>Clears fired alerts whose key starts with <paramref name="keyPrefix"/> — re-arms
    /// one module's thresholds (keys <c>module:{id}:*</c>) after its budget numbers change,
    /// without disturbing the rest.</summary>
    Task ClearForBudgetAsync(Guid budgetId, string keyPrefix, CancellationToken cancellationToken = default);
}
