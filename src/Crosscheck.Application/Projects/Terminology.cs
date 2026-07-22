using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

/// <summary>The project's word for its budget breakdown sections — "module" (MDEQ-style work
/// orders) or "milestone" (NRIS-style level-of-effort). Display only; code and schema always
/// say "module".</summary>
public static class Terminology
{
    public static string Singular(BreakdownLabel label) =>
        label == BreakdownLabel.Milestone ? "milestone" : "module";

    public static string Plural(BreakdownLabel label) =>
        label == BreakdownLabel.Milestone ? "milestones" : "modules";

    public static string SingularTitle(BreakdownLabel label) =>
        label == BreakdownLabel.Milestone ? "Milestone" : "Module";

    public static string PluralTitle(BreakdownLabel label) =>
        label == BreakdownLabel.Milestone ? "Milestones" : "Modules";
}
