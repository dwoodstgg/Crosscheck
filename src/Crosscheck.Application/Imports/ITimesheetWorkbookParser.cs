namespace Crosscheck.Application.Imports;

/// <summary>One workbook line with hours: the sheet/row it came from, the project label,
/// the date, the hours, and the typed work description (design-doc.md §6.6).</summary>
public record ParsedTimesheetRow(
    string SheetName,
    int SheetRow,
    string ProjectLabel,
    DateOnly Date,
    decimal Hours,
    string? Description);

/// <summary>What a parse produced: who/which year the workbook says it is, every row with
/// hours, and non-fatal oddities (skipped artifacts, calendar/description mismatches).</summary>
public record ParsedTimesheetWorkbook(
    string? EmployeeName,
    int? Year,
    IReadOnlyList<ParsedTimesheetRow> Rows,
    IReadOnlyList<string> Warnings);

/// <summary>Parses the company's Excel timesheet workbook. Hours + descriptions are read
/// from the per-month <c>*-DESC</c> sheets (one row per project per day, already rolled up
/// from the calendar); the calendar sheets are used only to cross-check monthly totals.
/// Defensive: skips hidden sheets, <c>#REF!</c>/<c>#VALUE!</c> artifacts, zero-hour rows,
/// and blank project slots, and dedupes repeated (project, date) rows.</summary>
public interface ITimesheetWorkbookParser
{
    ParsedTimesheetWorkbook Parse(Stream workbook);
}
