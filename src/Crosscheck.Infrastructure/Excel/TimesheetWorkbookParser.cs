using ClosedXML.Excel;
using Crosscheck.Application.Imports;
using Crosscheck.Domain;

namespace Crosscheck.Infrastructure.Excel;

/// <summary>Parses the company's Excel timesheet workbook (design-doc.md §6.6). Hours and
/// descriptions are read from each visible <c>*-DESC</c> sheet — one row per project per
/// day (column A label, C date, D hours, E description), already rolled up from the
/// calendar — and the matching calendar sheet is used only to cross-check per-project
/// monthly totals (a mismatch is a warning, never a failure). Hidden sheets, formula-error
/// artifacts (<c>#REF!</c>/<c>#VALUE!</c>), blank labels, and zero-hour rows are skipped;
/// repeated (project, date) rows are deduped with a warning.</summary>
public class TimesheetWorkbookParser : ITimesheetWorkbookParser
{
    private const string DescSuffix = "-DESC";

    public ParsedTimesheetWorkbook Parse(Stream workbook)
    {
        XLWorkbook xl;
        try
        {
            xl = new XLWorkbook(workbook);
        }
        catch (Exception ex)
        {
            throw new DomainException($"The file could not be read as an Excel workbook: {ex.Message}");
        }

        using (xl)
        {
            var warnings = new List<string>();
            var descSheets = xl.Worksheets
                .Where(ws => ws.Visibility == XLWorksheetVisibility.Visible
                             && ws.Name.Trim().EndsWith(DescSuffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (descSheets.Count == 0)
            {
                throw new DomainException(
                    "No monthly description sheets (JAN-DESC … DEC-DESC) were found — is this the company timesheet workbook?");
            }

            var employeeName = ReadEmployeeName(xl) ?? ReadCellText(descSheets[0], "D2");
            var year = ReadYear(xl, descSheets[0]);
            if (employeeName is null)
            {
                warnings.Add("The workbook does not name its employee — check the Yearly Info sheet.");
            }

            var rows = new List<ParsedTimesheetRow>();
            var byCell = new Dictionary<(string Label, DateOnly Date), ParsedTimesheetRow>();
            foreach (var sheet in descSheets)
            {
                ParseDescSheet(sheet, rows, byCell, warnings);
                CrossCheckCalendar(xl, sheet, byCell, warnings);
            }

            if (year is { } y)
            {
                var outside = rows.Count(r => r.Date.Year != y);
                if (outside > 0)
                {
                    warnings.Add($"{outside} entr{(outside == 1 ? "y falls" : "ies fall")} outside the workbook year {y} (weeks straddling New Year) — review the dates.");
                }
            }

            return new ParsedTimesheetWorkbook(employeeName, year, rows, warnings);
        }
    }

    /// <summary>Rows of a description sheet: day blocks repeating (label, weekday, date,
    /// hours, description); only rows with positive hours are entries.</summary>
    private static void ParseDescSheet(
        IXLWorksheet sheet,
        List<ParsedTimesheetRow> rows,
        Dictionary<(string, DateOnly), ParsedTimesheetRow> byCell,
        List<string> warnings)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 6; r <= lastRow; r++)
        {
            var hours = GetNumber(sheet.Cell(r, 4));
            if (hours is null or <= 0)
            {
                continue;
            }

            var label = GetText(sheet.Cell(r, 1));
            if (string.IsNullOrWhiteSpace(label) || label.StartsWith('#'))
            {
                warnings.Add($"{sheet.Name} row {r}: {hours} hours with no usable project label — row skipped.");
                continue;
            }

            var date = GetDate(sheet.Cell(r, 3));
            if (date is null)
            {
                warnings.Add($"{sheet.Name} row {r}: {hours} hours for \"{label}\" with no date — row skipped.");
                continue;
            }

            var parsed = new ParsedTimesheetRow(
                sheet.Name.Trim(), r, label,
                date.Value, decimal.Round((decimal)hours.Value, 2),
                GetText(sheet.Cell(r, 5)));

            var key = (label.ToLowerInvariant(), date.Value);
            if (byCell.TryGetValue(key, out var first))
            {
                warnings.Add(first.Hours == parsed.Hours
                    ? $"{sheet.Name} row {r}: \"{label}\" on {date:yyyy-MM-dd} repeats {first.SheetName} row {first.SheetRow} — duplicate skipped."
                    : $"{sheet.Name} row {r}: \"{label}\" on {date:yyyy-MM-dd} repeats {first.SheetName} row {first.SheetRow} with different hours ({parsed.Hours} vs {first.Hours}) — kept the first.");
                continue;
            }

            byCell[key] = parsed;
            rows.Add(parsed);
        }
    }

    /// <summary>Cross-checks a month's per-project totals against the top calendar block of
    /// the matching calendar sheet (e.g. JAN for JAN-DESC). The calendar is a matrix of
    /// project-label rows × day columns; the block ends at its "Total" row, which also keeps
    /// this away from any extra client-specific block further down (those are skipped by
    /// design). Restricted to the sheet's own month so week-straddle columns don't double.</summary>
    private static void CrossCheckCalendar(
        XLWorkbook xl,
        IXLWorksheet descSheet,
        Dictionary<(string, DateOnly), ParsedTimesheetRow> byCell,
        List<string> warnings)
    {
        var month = GetDate(descSheet.Cell(3, 4));
        if (month is null)
        {
            return;
        }

        var name = descSheet.Name.Trim();
        var calendarName = name[..^DescSuffix.Length];
        var calendar = xl.Worksheets.FirstOrDefault(ws =>
            ws.Visibility == XLWorksheetVisibility.Visible
            && string.Equals(ws.Name.Trim(), calendarName, StringComparison.OrdinalIgnoreCase));
        if (calendar is null)
        {
            warnings.Add($"{name}: no visible {calendarName} calendar sheet to cross-check against.");
            return;
        }

        // Locate the date header row (the first row with a date in column B) and map its
        // day columns to dates within this month.
        var dateColumns = new Dictionary<int, DateOnly>();
        var headerRow = 0;
        for (var r = 1; r <= 10 && headerRow == 0; r++)
        {
            if (GetDate(calendar.Cell(r, 2)) is not null)
            {
                headerRow = r;
            }
        }

        if (headerRow == 0)
        {
            warnings.Add($"{calendarName}: calendar layout not recognized — totals not cross-checked.");
            return;
        }

        var lastColumn = calendar.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 2; c <= lastColumn; c++)
        {
            if (GetDate(calendar.Cell(headerRow, c)) is { } d && d.Year == month.Value.Year && d.Month == month.Value.Month)
            {
                dateColumns[c] = d;
            }
        }

        // Project rows run from below the header to the block's "Total" row.
        var calendarTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var lastRow = calendar.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var label = GetText(calendar.Cell(r, 1));
            if (string.Equals(label, "Total", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(label) || label.StartsWith('#') || label.Contains("Job #", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Hours", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (column, _) in dateColumns)
            {
                if (GetNumber(calendar.Cell(r, column)) is { } h and > 0)
                {
                    calendarTotals[label] = calendarTotals.GetValueOrDefault(label) + decimal.Round((decimal)h, 2);
                }
            }
        }

        var descTotals = byCell.Values
            .Where(v => v.Date.Year == month.Value.Year && v.Date.Month == month.Value.Month)
            .GroupBy(v => v.ProjectLabel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(v => v.Hours), StringComparer.OrdinalIgnoreCase);

        foreach (var label in calendarTotals.Keys.Union(descTotals.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var onCalendar = calendarTotals.GetValueOrDefault(label);
            var onDesc = descTotals.GetValueOrDefault(label);
            if (onCalendar != onDesc)
            {
                warnings.Add($"{calendarName}: \"{label}\" totals {onCalendar:0.##}h on the calendar but {onDesc:0.##}h on {name} — the {name} hours are what imports.");
            }
        }
    }

    /// <summary>Employee name from Yearly Info: the cell below the "Employee Name" label.</summary>
    private static string? ReadEmployeeName(XLWorkbook xl)
    {
        var info = FindYearlyInfo(xl);
        if (info is null)
        {
            return null;
        }

        var lastRow = info.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= lastRow; r++)
        {
            if (string.Equals(GetText(info.Cell(r, 1)), "Employee Name", StringComparison.OrdinalIgnoreCase))
            {
                return GetText(info.Cell(r + 1, 1));
            }
        }

        return null;
    }

    private static int? ReadYear(XLWorkbook xl, IXLWorksheet firstDescSheet)
    {
        if (FindYearlyInfo(xl) is { } info && GetNumber(info.Cell(1, 2)) is { } fromInfo and >= 2000 and <= 2100)
        {
            return (int)fromInfo;
        }

        // Fallback: the DESC header carries "Year:" in I3 / value in K3.
        return GetNumber(firstDescSheet.Cell(3, 11)) is { } fromDesc and >= 2000 and <= 2100 ? (int)fromDesc : null;
    }

    private static IXLWorksheet? FindYearlyInfo(XLWorkbook xl) =>
        xl.Worksheets.FirstOrDefault(ws => string.Equals(ws.Name.Trim(), "Yearly Info", StringComparison.OrdinalIgnoreCase));

    private static string? ReadCellText(IXLWorksheet sheet, string address) => GetText(sheet.Cell(address));

    // Cell readers work from the file's cached values (never re-evaluating formulas — the
    // company workbook leans on functions no calc engine needs to support here).

    private static XLCellValue CachedValue(IXLCell cell) => cell.HasFormula ? cell.CachedValue : cell.Value;

    private static string? GetText(IXLCell cell)
    {
        var value = CachedValue(cell);
        if (value.IsError)
        {
            // #REF!/#VALUE! artifacts read as blank — the callers skip blank labels.
            return null;
        }

        var text = value.IsText ? value.GetText().Trim() : value.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static double? GetNumber(IXLCell cell)
    {
        var value = CachedValue(cell);
        return value.IsNumber ? value.GetNumber() : null;
    }

    private static DateOnly? GetDate(IXLCell cell)
    {
        var value = CachedValue(cell);
        if (value.IsDateTime)
        {
            return DateOnly.FromDateTime(value.GetDateTime());
        }

        // Dates sometimes surface as raw serial numbers when the format is unrecognized.
        if (value.IsNumber && value.GetNumber() is > 20000 and < 80000)
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(value.GetNumber()));
        }

        return null;
    }
}
