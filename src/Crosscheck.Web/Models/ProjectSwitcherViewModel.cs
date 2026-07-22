namespace Crosscheck.Web.Models;

public record ProjectSwitcherItem(Guid Id, string Label);

/// <summary>The searchable "Client - Project" dropdown that jumps to a project dashboard,
/// shared by the landing dashboard and the per-project dashboard header.</summary>
public record ProjectSwitcherViewModel(
    IReadOnlyList<ProjectSwitcherItem> Items,
    Guid? CurrentProjectId,
    string ButtonLabel = "Switch project",
    string ButtonClass = "btn btn-outline-secondary btn-sm");
