namespace Crosscheck.Domain.Enums;

/// <summary>What a project calls its budget breakdown — some work orders name the sections
/// "modules" (MDEQ Ag Chem / Water Levels), others "milestones" (MDWFP NRIS level-of-effort).
/// Purely presentational: mechanics are identical, and code/tables always say "module".</summary>
public enum BreakdownLabel
{
    Module,
    Milestone,
}
