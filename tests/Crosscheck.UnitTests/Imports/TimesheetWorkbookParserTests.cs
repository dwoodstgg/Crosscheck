using ClosedXML.Excel;
using Crosscheck.Domain;
using Crosscheck.Infrastructure.Excel;

namespace Crosscheck.UnitTests.Imports;

/// <summary>Parses the real company workbook (Samples/) plus synthetic workbooks for the
/// defensive edge cases (§6.6): hidden sheets, artifacts, duplicates, missing dates.</summary>
public class TimesheetWorkbookParserTests
{
    private static readonly string SamplePath =
        Path.Combine(AppContext.BaseDirectory, "Samples", "2026 Don Woods timesheet.xlsx");

    private readonly TimesheetWorkbookParser _parser = new();

    // ---- The real workbook ----

    [Fact]
    public void Parses_the_sample_workbook()
    {
        using var stream = File.OpenRead(SamplePath);

        var result = _parser.Parse(stream);

        Assert.Equal("Don Woods", result.EmployeeName);
        Assert.Equal(2026, result.Year);
        // Pinned to the checked-in sample: Jan–Jul are populated, Aug–Dec are empty.
        Assert.Equal(228, result.Rows.Count);
        Assert.Equal(1182.5m, result.Rows.Sum(r => r.Hours));
        Assert.All(result.Rows, r => Assert.True(r.Hours > 0));
        Assert.All(result.Rows, r => Assert.Equal(2026, r.Date.Year));
        // The hidden stale 'JAN ' calendar contributes nothing.
        Assert.All(result.Rows, r => Assert.EndsWith("-DESC", r.SheetName));
    }

    [Fact]
    public void Sample_rows_are_unique_per_project_and_day_with_trimmed_labels()
    {
        using var stream = File.OpenRead(SamplePath);

        var result = _parser.Parse(stream);

        Assert.Equal(result.Rows.Count, result.Rows.Select(r => (r.ProjectLabel.ToLowerInvariant(), r.Date)).Distinct().Count());
        Assert.All(result.Rows, r => Assert.Equal(r.ProjectLabel, r.ProjectLabel.Trim()));
        Assert.All(result.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.ProjectLabel)));
    }

    [Fact]
    public void Sample_january_matches_the_workbook()
    {
        using var stream = File.OpenRead(SamplePath);

        var result = _parser.Parse(stream);

        var january = result.Rows.Where(r => r.Date.Month == 1).ToList();
        Assert.Equal(37, january.Count);
        Assert.Equal(167.5m, january.Sum(r => r.Hours));

        var first = january.MinBy(r => r.Date)!;
        Assert.Equal(new DateOnly(2026, 1, 2), first.Date);
        Assert.Equal("NRIS-2026", first.ProjectLabel);
        Assert.Equal(6m, first.Hours);
        Assert.Equal("Burn Plan", first.Description);
    }

    // ---- Synthetic edge cases ----

    [Fact]
    public void Skips_hidden_desc_sheets()
    {
        var stream = BuildWorkbook(xl =>
        {
            var visible = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(visible, 7, "PRJ-1", new DateTime(2026, 1, 5), 6, "work");

            var hidden = AddDescSheet(xl, "FEB-DESC", new DateTime(2026, 2, 1));
            SetRow(hidden, 7, "PRJ-1", new DateTime(2026, 2, 3), 4, "stale");
            hidden.Visibility = XLWorksheetVisibility.Hidden;
        });

        var result = _parser.Parse(stream);

        var row = Assert.Single(result.Rows);
        Assert.Equal("JAN-DESC", row.SheetName);
    }

    [Fact]
    public void Skips_zero_hour_blank_label_and_error_rows()
    {
        var stream = BuildWorkbook(xl =>
        {
            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", new DateTime(2026, 1, 5), 0, null);          // zero hours
            SetRow(sheet, 8, "   ", new DateTime(2026, 1, 5), 3, "no label");      // blank label
            sheet.Cell(9, 1).Value = XLError.CellReference;                        // #REF! artifact
            sheet.Cell(9, 3).Value = new DateTime(2026, 1, 5);
            sheet.Cell(9, 4).Value = 2;
            SetRow(sheet, 10, "PRJ-1", new DateTime(2026, 1, 6), 5, "kept");
        });

        var result = _parser.Parse(stream);

        var row = Assert.Single(result.Rows);
        Assert.Equal(5m, row.Hours);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("no usable project label")));
    }

    [Fact]
    public void Dedupes_repeated_project_days_keeping_the_first()
    {
        var stream = BuildWorkbook(xl =>
        {
            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", new DateTime(2026, 1, 5), 6, "first");
            SetRow(sheet, 8, "prj-1", new DateTime(2026, 1, 5), 2, "second");
        });

        var result = _parser.Parse(stream);

        var row = Assert.Single(result.Rows);
        Assert.Equal(6m, row.Hours);
        Assert.Contains(result.Warnings, w => w.Contains("different hours"));
    }

    [Fact]
    public void Warns_and_skips_hours_without_a_date()
    {
        var stream = BuildWorkbook(xl =>
        {
            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", null, 6, "lost");
            SetRow(sheet, 8, "PRJ-1", new DateTime(2026, 1, 6), 2, "kept");
        });

        var result = _parser.Parse(stream);

        Assert.Single(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("no date"));
    }

    [Fact]
    public void Trims_descriptions_and_nulls_empty_ones()
    {
        var stream = BuildWorkbook(xl =>
        {
            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", new DateTime(2026, 1, 5), 6, "  padded  ");
            SetRow(sheet, 8, "PRJ-2", new DateTime(2026, 1, 5), 2, "   ");
        });

        var result = _parser.Parse(stream);

        Assert.Equal("padded", result.Rows.Single(r => r.ProjectLabel == "PRJ-1").Description);
        Assert.Null(result.Rows.Single(r => r.ProjectLabel == "PRJ-2").Description);
    }

    [Fact]
    public void Warns_when_calendar_and_desc_totals_disagree()
    {
        var stream = BuildWorkbook(xl =>
        {
            var calendar = xl.AddWorksheet("JAN");
            calendar.Cell(7, 2).Value = new DateTime(2026, 1, 5);
            calendar.Cell(9, 1).Value = "PRJ-1";
            calendar.Cell(9, 2).Value = 5; // calendar says 5h …
            calendar.Cell(10, 1).Value = "Total";

            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", new DateTime(2026, 1, 5), 3, "work"); // … DESC says 3h
        });

        var result = _parser.Parse(stream);

        Assert.Contains(result.Warnings, w => w.Contains("PRJ-1") && w.Contains("5") && w.Contains("3"));
    }

    [Fact]
    public void Warns_about_entries_outside_the_workbook_year()
    {
        var stream = BuildWorkbook(xl =>
        {
            var sheet = AddDescSheet(xl, "JAN-DESC", new DateTime(2026, 1, 1));
            SetRow(sheet, 7, "PRJ-1", new DateTime(2025, 12, 30), 4, "year straddle");
        });

        var result = _parser.Parse(stream);

        Assert.Single(result.Rows); // kept — the hours are real
        Assert.Contains(result.Warnings, w => w.Contains("outside the workbook year"));
    }

    [Fact]
    public void Rejects_a_workbook_without_desc_sheets()
    {
        var stream = BuildWorkbook(xl => xl.AddWorksheet("Sheet1").Cell(1, 1).SetValue("not a timesheet"));

        Assert.Throws<DomainException>(() => _parser.Parse(stream));
    }

    [Fact]
    public void Rejects_a_file_that_is_not_a_workbook()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);

        Assert.Throws<DomainException>(() => _parser.Parse(stream));
    }

    // ---- Builders ----

    private static MemoryStream BuildWorkbook(Action<XLWorkbook> build)
    {
        using var xl = new XLWorkbook();
        build(xl);
        var info = xl.AddWorksheet("Yearly Info");
        info.Cell(1, 1).Value = "Year";
        info.Cell(1, 2).Value = 2026;
        info.Cell(44, 1).Value = "Employee Name";
        info.Cell(45, 1).Value = "Test Person";

        var stream = new MemoryStream();
        xl.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static IXLWorksheet AddDescSheet(XLWorkbook xl, string name, DateTime month)
    {
        var sheet = xl.AddWorksheet(name);
        sheet.Cell(2, 2).Value = "Name:";
        sheet.Cell(2, 4).Value = "Test Person";
        sheet.Cell(3, 2).Value = "Month:";
        sheet.Cell(3, 4).Value = month;
        sheet.Cell(3, 9).Value = "Year:";
        sheet.Cell(3, 11).Value = month.Year;
        return sheet;
    }

    private static void SetRow(IXLWorksheet sheet, int row, string label, DateTime? date, double hours, string? description)
    {
        sheet.Cell(row, 1).Value = label;
        if (date is { } d)
        {
            sheet.Cell(row, 3).Value = d;
        }

        sheet.Cell(row, 4).Value = hours;
        if (description is not null)
        {
            sheet.Cell(row, 5).Value = description;
        }
    }
}
