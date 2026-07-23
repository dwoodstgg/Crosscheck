using Crosscheck.Application.Imports;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Imports;

public class TimesheetImportServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeTimesheetWorkbookParser _parser = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeTimesheetImportRepository _imports;
    private readonly FakeBudgetAlertService _budgetAlerts = new();
    private readonly FakeAuditLog _audit = new();
    private readonly TimesheetImportService _service;

    private readonly Client _client;
    private readonly Project _project;
    private readonly Role _developer;
    private readonly Employee _don;
    private static readonly DateOnly Day = new(2026, 1, 5);

    public TimesheetImportServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _imports = new FakeTimesheetImportRepository(_entries);
        _service = new TimesheetImportService(
            _currentUser, _parser, _imports, _employees, _projects, _clients,
            _assignments, _roles, _entries, _budgetAlerts, _audit);

        _currentUser.Roles.Add(RoleNames.OperationsManager);

        _client = new Client { Id = Guid.NewGuid(), Name = "MDWFP" };
        _clients.Clients.Add(_client);
        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = _client.Id,
            Name = "NRIS 2026",
            Code = "NRIS-2026",
            Status = ProjectStatus.Active,
            ProjectManagerId = Guid.NewGuid(),
        };
        _projects.Projects.Add(_project);

        _developer = new Role { Id = Guid.NewGuid(), Name = RoleNames.Developer, DisplayName = "Developer", IsBillable = true };
        _roles.Roles.Add(_developer);

        _don = new Employee { Id = Guid.NewGuid(), Email = "dwoods@thegeospatialgroup.com", DisplayName = "Don Woods" };
        _employees.Employees.Add(_don);
        _employees.RoleIdsByEmployee[_don.Id] = [_developer.Id];
    }

    private static ParsedTimesheetRow Row(
        string label, DateOnly date, decimal hours, string? description = "did work", int sheetRow = 100) =>
        new("JAN-DESC", sheetRow, label, date, hours, description);

    private ParsedTimesheetWorkbook Workbook(params ParsedTimesheetRow[] rows) =>
        new("Don Woods", 2026, rows, []);

    private Task<Guid> UploadAsync() =>
        _service.UploadAsync(Stream.Null, "timesheet.xlsx", _don.Email);

    // ---- Upload ----

    [Fact]
    public async Task Upload_requires_operations_manager()
    {
        _currentUser.Roles.Clear();
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(UploadAsync);
    }

    [Fact]
    public async Task Upload_maps_labels_by_project_code_and_defaults_the_billing_role()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));

        var importId = await UploadAsync();

        var row = Assert.Single(_imports.Rows);
        Assert.Equal(importId, row.ImportId);
        Assert.Equal(_project.Id, row.ProjectId);
        // Don holds exactly one billable role, so it becomes the default.
        Assert.Equal(_developer.Id, row.BillingRoleId);
        Assert.True(row.Included);
        Assert.Equal(6m, row.Hours);
    }

    [Fact]
    public async Task Upload_maps_labels_through_saved_mappings_when_no_code_matches()
    {
        _imports.Mappings.Add(new TimesheetImportMapping
        {
            Id = Guid.NewGuid(),
            Label = "Burn Plan Work",
            ProjectId = _project.Id,
        });
        _parser.Result = Workbook(Row("burn plan work", Day, 4));

        await UploadAsync();

        Assert.Equal(_project.Id, Assert.Single(_imports.Rows).ProjectId);
    }

    [Fact]
    public async Task Upload_leaves_unknown_labels_unmapped()
    {
        _parser.Result = Workbook(Row("SOMETHING-ELSE", Day, 4));

        await UploadAsync();

        var row = Assert.Single(_imports.Rows);
        Assert.Null(row.ProjectId);
        Assert.Null(row.BillingRoleId);
    }

    [Fact]
    public async Task Upload_prefers_the_assignments_default_billing_role()
    {
        var pm = new Role { Id = Guid.NewGuid(), Name = RoleNames.ProjectManager, DisplayName = "Project Manager", IsBillable = true };
        _roles.Roles.Add(pm);
        _assignments.Assignments.Add(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            DefaultBillingRoleId = pm.Id,
        });
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));

        await UploadAsync();

        Assert.Equal(pm.Id, Assert.Single(_imports.Rows).BillingRoleId);
    }

    [Fact]
    public async Task Upload_excludes_rows_that_duplicate_an_existing_entry()
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            BillingRoleId = _developer.Id,
            EntryDate = Day,
            HoursWorked = 8,
            HoursBilled = 8,
        });
        _parser.Result = Workbook(
            Row("NRIS-2026", Day, 6),
            Row("NRIS-2026", Day.AddDays(1), 4, sheetRow: 130));

        await UploadAsync();

        Assert.False(_imports.Rows.Single(r => r.EntryDate == Day).Included);
        Assert.True(_imports.Rows.Single(r => r.EntryDate == Day.AddDays(1)).Included);
    }

    [Fact]
    public async Task Upload_creates_the_employee_when_the_email_is_new()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));

        await _service.UploadAsync(Stream.Null, "timesheet.xlsx", "newhire@thegeospatialgroup.com");

        var created = Assert.Single(_employees.Added);
        Assert.Equal("newhire@thegeospatialgroup.com", created.Email);
        Assert.Equal("Don Woods", created.DisplayName); // from the workbook
        Assert.Null(created.EntraOid);
        Assert.Contains(_audit.Events, e => e.Action == "import.employee_created");
    }

    [Fact]
    public async Task Upload_rejects_a_workbook_with_no_hours()
    {
        _parser.Result = Workbook();

        await Assert.ThrowsAsync<DomainException>(UploadAsync);
    }

    // ---- Review ----

    [Fact]
    public async Task Review_flags_duplicates_and_problems()
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            BillingRoleId = _developer.Id,
            EntryDate = Day,
            HoursWorked = 8,
            HoursBilled = 8,
        });
        _parser.Result = Workbook(
            Row("NRIS-2026", Day, 6),                            // duplicate of existing
            Row("UNKNOWN", Day.AddDays(1), 4),                   // unmapped
            Row("NRIS-2026", Day.AddDays(2), 8, description: null)); // billable, no description
        var importId = await UploadAsync();

        var review = await _service.GetReviewAsync(importId);

        Assert.Equal(3, review.Rows.Count);
        var duplicate = review.Rows.Single(r => r.Row.EntryDate == Day);
        Assert.True(duplicate.DuplicateOfExisting);
        Assert.False(duplicate.Row.Included);

        var unmapped = review.Rows.Single(r => r.Row.EntryDate == Day.AddDays(1));
        Assert.Contains(unmapped.Problems, p => p.Contains("No project matches"));

        var noDescription = review.Rows.Single(r => r.Row.EntryDate == Day.AddDays(2));
        Assert.Contains(noDescription.Problems, p => p.Contains("description"));
    }

    [Fact]
    public async Task Review_flags_the_later_of_two_included_rows_on_the_same_cell()
    {
        _parser.Result = Workbook(
            Row("NRIS-2026", Day, 6, sheetRow: 100),
            Row("NRIS-2026", Day, 2, sheetRow: 101));
        var importId = await UploadAsync();

        var review = await _service.GetReviewAsync(importId);

        Assert.False(review.Rows[0].DuplicateInFile);
        Assert.True(review.Rows[1].DuplicateInFile);
    }

    [Fact]
    public async Task Review_warns_about_closed_projects_without_blocking()
    {
        _project.Status = ProjectStatus.Closed;
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        var review = await _service.GetReviewAsync(importId);

        var row = Assert.Single(review.Rows);
        Assert.Empty(row.Problems);
        Assert.Contains(row.Warnings, w => w.Contains("closed"));
    }

    // ---- Row edits ----

    [Fact]
    public async Task UpdateRow_applies_edits()
    {
        _parser.Result = Workbook(Row("UNKNOWN", Day, 6));
        await UploadAsync();
        var row = Assert.Single(_imports.Rows);

        await _service.UpdateRowAsync(row.Id, _project.Id, _developer.Id, Day.AddDays(1), 7.25m, "  mapped work  ", true);

        var updated = Assert.Single(_imports.Rows);
        Assert.Equal(_project.Id, updated.ProjectId);
        Assert.Equal(_developer.Id, updated.BillingRoleId);
        Assert.Equal(Day.AddDays(1), updated.EntryDate);
        Assert.Equal(7.25m, updated.Hours);
        Assert.Equal("mapped work", updated.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(6.2)]
    public async Task UpdateRow_rejects_invalid_hours(double hours)
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        await UploadAsync();
        var row = Assert.Single(_imports.Rows);

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateRowAsync(row.Id, _project.Id, _developer.Id, Day, (decimal)hours, "work", true));
    }

    [Fact]
    public async Task UpdateRow_rejects_a_non_billable_role()
    {
        var admin = new Role { Id = Guid.NewGuid(), Name = RoleNames.Admin, DisplayName = "Admin", IsBillable = false, IsSystemAdmin = true };
        _roles.Roles.Add(admin);
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        await UploadAsync();
        var row = Assert.Single(_imports.Rows);

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateRowAsync(row.Id, _project.Id, admin.Id, Day, 6, "work", true));
    }

    [Fact]
    public async Task ExcludeDuplicates_excludes_only_flagged_rows()
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            BillingRoleId = _developer.Id,
            EntryDate = Day,
            HoursWorked = 8,
            HoursBilled = 8,
        });
        _parser.Result = Workbook(
            Row("NRIS-2026", Day, 6),
            Row("NRIS-2026", Day.AddDays(1), 4, sheetRow: 130));
        var importId = await UploadAsync();
        // Re-include the duplicate so the bulk action has something to do.
        _imports.Rows.Single(r => r.EntryDate == Day).Included = true;

        var excluded = await _service.ExcludeDuplicatesAsync(importId);

        Assert.Equal(1, excluded);
        Assert.False(_imports.Rows.Single(r => r.EntryDate == Day).Included);
        Assert.True(_imports.Rows.Single(r => r.EntryDate == Day.AddDays(1)).Included);
    }

    // ---- Commit ----

    [Fact]
    public async Task Commit_creates_approved_entries_with_the_import_id()
    {
        _parser.Result = Workbook(
            Row("NRIS-2026", Day, 6),
            Row("NRIS-2026", Day.AddDays(1), 4.5m, sheetRow: 130));
        var importId = await UploadAsync();

        var result = await _service.CommitAsync(importId);

        Assert.Equal(2, result.EntriesCreated);
        Assert.Equal(10.5m, result.TotalHours);
        Assert.Equal(2, _entries.Entries.Count);
        Assert.All(_entries.Entries, e =>
        {
            Assert.Equal(TimeEntryStatus.Approved, e.Status);
            Assert.Equal(importId, e.ImportId);
            Assert.Null(e.ModuleId); // historical entries land in the unassigned bucket
            Assert.Equal(_don.Id, e.EmployeeId);
            Assert.Equal(_currentUser.EmployeeId, e.ApprovedById);
            Assert.True(e.IsBillable);
            Assert.Equal(e.HoursWorked, e.HoursBilled);
        });
        Assert.Equal(TimesheetImportStatus.Committed, _imports.Imports.Single().Status);
        Assert.Contains(_audit.Events, e => e.Action == "import.committed");
        Assert.Contains(_project.Id, _budgetAlerts.Evaluated);
    }

    [Fact]
    public async Task Commit_auto_creates_the_missing_assignment()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        var result = await _service.CommitAsync(importId);

        Assert.Equal(1, result.AssignmentsCreated);
        var assignment = Assert.Single(_imports.CommittedAssignments);
        Assert.Equal(_project.Id, assignment.ProjectId);
        Assert.Equal(_don.Id, assignment.EmployeeId);
    }

    [Fact]
    public async Task Commit_leaves_an_ended_assignment_ended()
    {
        _assignments.Assignments.Add(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            EndDate = new DateOnly(2026, 3, 1),
        });
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        var result = await _service.CommitAsync(importId);

        Assert.Equal(0, result.AssignmentsCreated);
        Assert.NotNull(_assignments.Assignments.Single().EndDate);
    }

    [Fact]
    public async Task Commit_remembers_hand_mapped_labels()
    {
        _parser.Result = Workbook(Row("Burn Plan Work", Day, 6));
        var importId = await UploadAsync();
        var row = Assert.Single(_imports.Rows);
        await _service.UpdateRowAsync(row.Id, _project.Id, _developer.Id, row.EntryDate, row.Hours, row.Description, true);

        var result = await _service.CommitAsync(importId);

        Assert.Equal(1, result.MappingsSaved);
        var mapping = Assert.Single(_imports.Mappings);
        Assert.Equal("Burn Plan Work", mapping.Label);
        Assert.Equal(_project.Id, mapping.ProjectId);
    }

    [Fact]
    public async Task Commit_saves_no_mapping_when_the_label_is_the_project_code()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        var result = await _service.CommitAsync(importId);

        Assert.Equal(0, result.MappingsSaved);
        Assert.Empty(_imports.Mappings);
    }

    [Fact]
    public async Task Commit_marks_internal_project_time_non_billable()
    {
        _client.IsInternal = true;
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6, description: null));
        var importId = await UploadAsync();

        await _service.CommitAsync(importId);

        Assert.False(Assert.Single(_entries.Entries).IsBillable);
    }

    [Fact]
    public async Task Commit_blocks_while_an_included_row_is_unmapped()
    {
        _parser.Result = Workbook(Row("UNKNOWN", Day, 6));
        var importId = await UploadAsync();

        await Assert.ThrowsAsync<DomainException>(() => _service.CommitAsync(importId));
        Assert.Empty(_entries.Entries);
        Assert.Equal(TimesheetImportStatus.Pending, _imports.Imports.Single().Status);
    }

    [Fact]
    public async Task Commit_blocks_while_an_included_row_duplicates_an_existing_entry()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();
        // An entry lands on the same cell between upload and commit.
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _don.Id,
            BillingRoleId = _developer.Id,
            EntryDate = Day,
            HoursWorked = 8,
            HoursBilled = 8,
        });

        await Assert.ThrowsAsync<DomainException>(() => _service.CommitAsync(importId));
    }

    [Fact]
    public async Task Commit_rejects_an_already_committed_import()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();
        await _service.CommitAsync(importId);

        await Assert.ThrowsAsync<DomainException>(() => _service.CommitAsync(importId));
    }

    // ---- Rollback / discard ----

    [Fact]
    public async Task Rollback_removes_the_imports_entries()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();
        await _service.CommitAsync(importId);

        await _service.RollbackAsync(importId);

        Assert.Empty(_entries.Entries);
        Assert.Equal(TimesheetImportStatus.RolledBack, _imports.Imports.Single().Status);
        Assert.Contains(_audit.Events, e => e.Action == "import.rolled_back");
    }

    [Fact]
    public async Task Rollback_blocks_while_an_entry_is_invoiced()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();
        await _service.CommitAsync(importId);
        _entries.Entries[0].Status = TimeEntryStatus.Invoiced;

        await Assert.ThrowsAsync<DomainException>(() => _service.RollbackAsync(importId));
        Assert.Single(_entries.Entries);
    }

    [Fact]
    public async Task Rollback_rejects_a_pending_import()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        await Assert.ThrowsAsync<DomainException>(() => _service.RollbackAsync(importId));
    }

    [Fact]
    public async Task Discard_deletes_a_pending_import()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();

        await _service.DiscardAsync(importId);

        Assert.Empty(_imports.Imports);
        Assert.Empty(_imports.Rows);
        Assert.Contains(_audit.Events, e => e.Action == "import.discarded");
    }

    [Fact]
    public async Task Discard_rejects_a_committed_import()
    {
        _parser.Result = Workbook(Row("NRIS-2026", Day, 6));
        var importId = await UploadAsync();
        await _service.CommitAsync(importId);

        await Assert.ThrowsAsync<DomainException>(() => _service.DiscardAsync(importId));
    }
}
