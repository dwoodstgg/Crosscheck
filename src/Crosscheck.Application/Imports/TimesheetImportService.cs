using Crosscheck.Application.Clients;
using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Application.Projects;
using Crosscheck.Application.Roles;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Imports;

/// <summary>One staging row with its review flags. <paramref name="DuplicateOfExisting"/>
/// marks a row whose (project, date) already has a time entry for the employee outside this
/// import; <paramref name="DuplicateInFile"/> marks the later of two included rows landing
/// on the same (project, date). <paramref name="Problems"/> block commit;
/// <paramref name="Warnings"/> (e.g. a closed project) inform but don't.</summary>
public record ImportReviewRow(
    TimesheetImportRow Row,
    bool DuplicateOfExisting,
    bool DuplicateInFile,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings);

/// <summary>Everything the review page shows: the import, whose time it is, every row with
/// flags, and the option lists for the project/billing-role dropdowns.</summary>
public record ImportReview(
    TimesheetImport Import,
    string EmployeeName,
    string EmployeeEmail,
    IReadOnlyList<ImportReviewRow> Rows,
    IReadOnlyList<ProjectSummary> ProjectOptions,
    IReadOnlyList<Role> BillingRoleOptions)
{
    public int IncludedCount => Rows.Count(r => r.Row.Included);
    public decimal IncludedHours => Rows.Where(r => r.Row.Included).Sum(r => r.Row.Hours);
    public int BlockedCount => Rows.Count(r => r.Row.Included && r.Problems.Count > 0);
    public int DuplicateCount => Rows.Count(r => r.DuplicateOfExisting || r.DuplicateInFile);
}

public record ImportCommitResult(int EntriesCreated, decimal TotalHours, int AssignmentsCreated, int MappingsSaved);

/// <summary>Excel timesheet import (design-doc.md §6.6): upload → parse into staging rows →
/// review/edit every line (project mapping, hours, description, billing role, include) →
/// commit → rollback while nothing is invoiced. Ops/Admin only. Committed entries are
/// created <c>approved</c> (historical data is treated as already approved), land in the
/// module-less "unassigned" bucket, and skip the open-window and active-assignment gates that
/// govern live entry — a missing assignment is auto-created (audited) instead.</summary>
public class TimesheetImportService(
    ICurrentUser currentUser,
    ITimesheetWorkbookParser parser,
    ITimesheetImportRepository imports,
    IEmployeeRepository employees,
    IProjectRepository projects,
    IClientRepository clients,
    IAssignmentRepository assignments,
    IRoleRepository roles,
    ITimeEntryRepository entries,
    IBudgetAlertService budgetAlerts,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<TimesheetImportSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager);
        return await imports.GetAllAsync(cancellationToken);
    }

    /// <summary>Parses the uploaded workbook into a pending import for review. The employee
    /// is resolved by tenant <paramref name="employeeEmail"/> — created (email-only, no
    /// roles, Entra-linked on first sign-in) when no record exists. Project labels auto-map
    /// by project code first, then saved mappings; rows that collide with an existing entry
    /// start excluded so nothing is silently doubled.</summary>
    public async Task<Guid> UploadAsync(
        Stream workbook, string fileName, string employeeEmail, CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        employeeEmail = employeeEmail.Trim();
        if (!employeeEmail.Contains('@'))
        {
            throw new DomainException("Enter the employee's tenant email address.");
        }

        var parsed = parser.Parse(workbook);
        if (parsed.Rows.Count == 0)
        {
            throw new DomainException("No time entries with hours were found in this workbook.");
        }

        var employee = await employees.GetByEmailAsync(employeeEmail, cancellationToken);
        if (employee is null)
        {
            employee = new Employee
            {
                Id = Guid.NewGuid(),
                Email = employeeEmail,
                DisplayName = string.IsNullOrWhiteSpace(parsed.EmployeeName) ? employeeEmail : parsed.EmployeeName.Trim(),
            };
            await employees.AddAsync(employee, cancellationToken);
            await audit.WriteAsync(new AuditEvent(
                currentUser.EmployeeId, "import.employee_created", "employee", employee.Id,
                new { employee.Email, employee.DisplayName, adminOverride }), cancellationToken);
        }

        var import = new TimesheetImport
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            FileName = fileName,
            Year = parsed.Year ?? parsed.Rows.Max(r => r.Date.Year),
            ParseWarnings = parsed.Warnings.Count == 0 ? null : string.Join('\n', parsed.Warnings),
            UploadedById = currentUser.EmployeeId ?? throw new UnauthorizedAccessException("No signed-in employee."),
            UploadedAt = DateTimeOffset.UtcNow,
        };

        var projectByLabel = await AutoMapLabelsAsync(parsed.Rows, cancellationToken);
        var defaultRoleByProject = await ResolveDefaultRolesAsync(employee.Id, projectByLabel.Values, cancellationToken);
        var existingCells = await GetExistingCellsAsync(
            employee.Id, parsed.Rows.Min(r => r.Date), parsed.Rows.Max(r => r.Date), import.Id, cancellationToken);

        var rows = new List<TimesheetImportRow>(parsed.Rows.Count);
        foreach (var parsedRow in parsed.Rows)
        {
            var project = projectByLabel.GetValueOrDefault(Normalize(parsedRow.ProjectLabel));
            var isDuplicate = project is not null && existingCells.Contains((project.Id, parsedRow.Date));
            rows.Add(new TimesheetImportRow
            {
                Id = Guid.NewGuid(),
                ImportId = import.Id,
                SheetName = parsedRow.SheetName,
                SheetRow = parsedRow.SheetRow,
                ProjectLabel = parsedRow.ProjectLabel,
                EntryDate = parsedRow.Date,
                Hours = parsedRow.Hours,
                Description = parsedRow.Description,
                ProjectId = project?.Id,
                BillingRoleId = project is null ? null : defaultRoleByProject.GetValueOrDefault(project.Id),
                Included = !isDuplicate,
            });
        }

        await imports.AddAsync(import, rows, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "import.uploaded", "timesheet_import", import.Id,
            new
            {
                Employee = employee.Email,
                import.FileName,
                import.Year,
                Rows = rows.Count,
                Hours = rows.Sum(r => r.Hours),
                adminOverride,
            }), cancellationToken);

        return import.Id;
    }

    public async Task<ImportReview> GetReviewAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager);

        var import = await imports.GetAsync(importId, cancellationToken)
            ?? throw new DomainException("Unknown import.");
        var employee = await employees.GetByIdAsync(import.EmployeeId, cancellationToken)
            ?? throw new DomainException("The import's employee no longer exists.");
        var rows = await imports.GetRowsAsync(importId, cancellationToken);
        var allProjects = await projects.GetAllAsync(cancellationToken);
        var billableRoles = (await roles.GetAllAsync(cancellationToken)).Where(r => r.IsBillable).ToList();

        var existingCells = rows.Count == 0
            ? []
            : await GetExistingCellsAsync(
                import.EmployeeId, rows.Min(r => r.EntryDate), rows.Max(r => r.EntryDate), import.Id, cancellationToken);

        var projectById = allProjects.ToDictionary(p => p.Project.Id, p => p.Project);
        var internalClientIds = (await clients.GetAllAsync(cancellationToken))
            .Where(c => c.IsInternal).Select(c => c.Id).ToHashSet();

        var seenCells = new HashSet<(Guid, DateOnly)>();
        var reviewRows = new List<ImportReviewRow>(rows.Count);
        foreach (var row in rows)
        {
            var duplicateOfExisting = row.ProjectId is { } pid && existingCells.Contains((pid, row.EntryDate));
            // Only included rows compete for a cell — the first keeps it, later ones flag.
            var duplicateInFile = row is { Included: true, ProjectId: { } p } && !seenCells.Add((p, row.EntryDate));

            var problems = new List<string>();
            var warnings = new List<string>();
            var project = row.ProjectId is { } id ? projectById.GetValueOrDefault(id) : null;
            if (project is null)
            {
                problems.Add($"No project matches \"{row.ProjectLabel}\" — pick one (the choice is remembered).");
            }
            else
            {
                if (row.BillingRoleId is null)
                {
                    problems.Add("Pick a billing role.");
                }

                var isBillable = project.Type != ProjectType.Internal && !internalClientIds.Contains(project.ClientId);
                if (isBillable && string.IsNullOrWhiteSpace(row.Description))
                {
                    problems.Add("Billable time needs a work description.");
                }

                if (project.Status is ProjectStatus.Closed or ProjectStatus.Archived)
                {
                    warnings.Add($"Project {project.Code} is {project.Status.ToString().ToLowerInvariant()} — historical import is allowed, but double-check.");
                }
            }

            if (!ValidHours(row.Hours, out var hoursProblem))
            {
                problems.Add(hoursProblem);
            }

            reviewRows.Add(new ImportReviewRow(row, duplicateOfExisting, duplicateInFile, problems, warnings));
        }

        return new ImportReview(import, employee.DisplayName, employee.Email, reviewRows, allProjects, billableRoles);
    }

    /// <summary>Applies the importer's edits to one staging row. Everything the commit uses
    /// is editable while the import is pending.</summary>
    public async Task UpdateRowAsync(
        Guid rowId, Guid? projectId, Guid? billingRoleId, DateOnly entryDate, decimal hours,
        string? description, bool included, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager);

        var row = await imports.GetRowAsync(rowId, cancellationToken)
            ?? throw new DomainException("Unknown import row.");
        await RequirePendingAsync(row.ImportId, cancellationToken);

        if (!ValidHours(hours, out var hoursProblem))
        {
            throw new DomainException(hoursProblem);
        }

        if (projectId is { } pid && await projects.GetByIdAsync(pid, cancellationToken) is null)
        {
            throw new DomainException("Unknown project.");
        }

        if (billingRoleId is { } rid)
        {
            var role = await roles.GetByIdAsync(rid, cancellationToken)
                ?? throw new DomainException("Unknown billing role.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role.");
            }
        }

        row.ProjectId = projectId;
        row.BillingRoleId = billingRoleId;
        row.EntryDate = entryDate;
        row.Hours = hours;
        row.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        row.Included = included;
        await imports.UpdateRowAsync(row, cancellationToken);
    }

    /// <summary>Excludes every included row flagged as a duplicate (of an existing entry, or
    /// of an earlier row in the file). Returns how many rows were excluded.</summary>
    public async Task<int> ExcludeDuplicatesAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        var review = await GetReviewAsync(importId, cancellationToken);
        await RequirePendingAsync(importId, cancellationToken);

        var duplicateIds = review.Rows
            .Where(r => r.Row.Included && (r.DuplicateOfExisting || r.DuplicateInFile))
            .Select(r => r.Row.Id)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            await imports.SetRowsIncludedAsync(duplicateIds, false, cancellationToken);
        }

        return duplicateIds.Count;
    }

    /// <summary>Commits every included row as an <c>approved</c> time entry tagged with the
    /// import id (module-less "unassigned" bucket). Blocks while any included row still has
    /// a problem or lands on a cell that already has an entry — nothing is silently doubled.
    /// Missing assignments are auto-created and hand-mapped labels are remembered, all in one
    /// transaction with the entries.</summary>
    public async Task<ImportCommitResult> CommitAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var review = await GetReviewAsync(importId, cancellationToken);
        var import = review.Import;
        if (import.Status != TimesheetImportStatus.Pending)
        {
            throw new DomainException("Only a pending import can be committed.");
        }

        var included = review.Rows.Where(r => r.Row.Included).ToList();
        if (included.Count == 0)
        {
            throw new DomainException("No rows are included — nothing to commit.");
        }

        var blockers = new List<string>();
        var blocked = included.Where(r => r.Problems.Count > 0).ToList();
        if (blocked.Count > 0)
        {
            blockers.Add($"{blocked.Count} included row{(blocked.Count == 1 ? " has" : "s have")} unresolved problems (unmapped project, missing billing role or description).");
        }

        var collisions = included.Count(r => r.DuplicateOfExisting || r.DuplicateInFile);
        if (collisions > 0)
        {
            blockers.Add($"{collisions} included row{(collisions == 1 ? "" : "s")} would duplicate an existing entry — exclude or edit them.");
        }

        if (blockers.Count > 0)
        {
            throw new DomainException(string.Join(" ", blockers));
        }

        var projectById = review.ProjectOptions.ToDictionary(p => p.Project.Id, p => p.Project);
        var internalClientIds = (await clients.GetAllAsync(cancellationToken))
            .Where(c => c.IsInternal).Select(c => c.Id).ToHashSet();

        var approverId = currentUser.EmployeeId;
        var now = DateTimeOffset.UtcNow;
        var newEntries = included.Select(r =>
        {
            var project = projectById[r.Row.ProjectId!.Value];
            return new TimeEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                ModuleId = null,
                EmployeeId = import.EmployeeId,
                BillingRoleId = r.Row.BillingRoleId!.Value,
                EntryDate = r.Row.EntryDate,
                HoursWorked = r.Row.Hours,
                HoursBilled = r.Row.Hours,
                Notes = r.Row.Description,
                IsBillable = project.Type != ProjectType.Internal && !internalClientIds.Contains(project.ClientId),
                Status = TimeEntryStatus.Approved,
                ApprovedById = approverId,
                ApprovedAt = now,
                ImportId = import.Id,
            };
        }).ToList();

        // Historical time may predate the roster: create missing assignments outright, but
        // leave an ended (soft-deactivated) assignment ended — the entries are history, and
        // reactivating would let the person log new time.
        var newAssignments = new List<ProjectAssignment>();
        var projectIds = newEntries.Select(e => e.ProjectId).Distinct().ToList();
        foreach (var projectId in projectIds)
        {
            var assignment = await assignments.GetByProjectAndEmployeeAsync(projectId, import.EmployeeId, cancellationToken);
            if (assignment is null)
            {
                newAssignments.Add(new ProjectAssignment
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    EmployeeId = import.EmployeeId,
                });
            }
        }

        // Remember every label that doesn't simply equal its project's code, so the next
        // workbook auto-maps it (decided 2026-07-23: map inline on review, remember it).
        var existingMappings = (await imports.GetMappingsAsync(cancellationToken))
            .ToDictionary(m => Normalize(m.Label), m => m.ProjectId);
        var newMappings = included
            .GroupBy(r => Normalize(r.Row.ProjectLabel))
            .Select(g => (Label: g.First().Row.ProjectLabel, ProjectId: g.Last().Row.ProjectId!.Value, Key: g.Key))
            .Where(m => !string.Equals(projectById[m.ProjectId].Code.Trim(), m.Label, StringComparison.OrdinalIgnoreCase))
            .Where(m => existingMappings.GetValueOrDefault(m.Key) != m.ProjectId)
            .Select(m => new TimesheetImportMapping
            {
                Id = Guid.NewGuid(),
                Label = m.Label,
                ProjectId = m.ProjectId,
                CreatedById = currentUser.EmployeeId,
                CreatedAt = now,
            })
            .ToList();

        import.Status = TimesheetImportStatus.Committed;
        import.CommittedById = currentUser.EmployeeId;
        import.CommittedAt = now;
        await imports.CommitAsync(import, newEntries, newAssignments, newMappings, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "import.committed", "timesheet_import", import.Id,
            new
            {
                Employee = review.EmployeeEmail,
                import.FileName,
                Entries = newEntries.Count,
                Hours = newEntries.Sum(e => e.HoursWorked),
                AssignmentsCreated = newAssignments.Count,
                adminOverride,
            }), cancellationToken);

        foreach (var projectId in projectIds)
        {
            await budgetAlerts.EvaluateAsync(projectId, cancellationToken);
        }

        return new ImportCommitResult(
            newEntries.Count, newEntries.Sum(e => e.HoursWorked), newAssignments.Count, newMappings.Count);
    }

    /// <summary>Reverses a committed import — deletes its entries — while none are invoiced
    /// (design rule: void the invoice first).</summary>
    public async Task RollbackAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var import = await imports.GetAsync(importId, cancellationToken)
            ?? throw new DomainException("Unknown import.");
        if (import.Status != TimesheetImportStatus.Committed)
        {
            throw new DomainException("Only a committed import can be rolled back.");
        }

        if (await imports.HasInvoicedEntriesAsync(importId, cancellationToken))
        {
            throw new DomainException("Some of this import's entries are on an invoice — void the invoice before rolling back.");
        }

        var projectIds = (await imports.GetRowsAsync(importId, cancellationToken))
            .Where(r => r is { Included: true, ProjectId: not null })
            .Select(r => r.ProjectId!.Value)
            .Distinct()
            .ToList();

        import.Status = TimesheetImportStatus.RolledBack;
        import.RolledBackById = currentUser.EmployeeId;
        import.RolledBackAt = DateTimeOffset.UtcNow;
        await imports.RollbackAsync(import, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "import.rolled_back", "timesheet_import", import.Id,
            new { import.FileName, adminOverride }), cancellationToken);

        foreach (var projectId in projectIds)
        {
            await budgetAlerts.EvaluateAsync(projectId, cancellationToken);
        }
    }

    /// <summary>Discards a pending import (hard-delete — no entries exist yet).</summary>
    public async Task DiscardAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var import = await imports.GetAsync(importId, cancellationToken)
            ?? throw new DomainException("Unknown import.");
        if (import.Status != TimesheetImportStatus.Pending)
        {
            throw new DomainException("Only a pending import can be discarded — roll back a committed one instead.");
        }

        await imports.DeleteAsync(importId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "import.discarded", "timesheet_import", import.Id,
            new { import.FileName, adminOverride }), cancellationToken);
    }

    /// <summary>Matches each distinct workbook label to a project: exact code match wins
    /// (codes are globally unique for exactly this reason — migration 0022), then a saved
    /// mapping from an earlier import. Unmatched labels stay null for manual mapping.</summary>
    private async Task<Dictionary<string, Project>> AutoMapLabelsAsync(
        IReadOnlyList<ParsedTimesheetRow> rows, CancellationToken cancellationToken)
    {
        var mappings = (await imports.GetMappingsAsync(cancellationToken))
            .ToDictionary(m => Normalize(m.Label), m => m.ProjectId);

        var result = new Dictionary<string, Project>();
        foreach (var label in rows.Select(r => r.ProjectLabel).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var project = await projects.GetByCodeAsync(label.Trim(), cancellationToken);
            if (project is null && mappings.TryGetValue(Normalize(label), out var mappedId))
            {
                project = await projects.GetByIdAsync(mappedId, cancellationToken);
            }

            if (project is not null)
            {
                result[Normalize(label)] = project;
            }
        }

        return result;
    }

    /// <summary>Default billing role per project: the assignment's default when one exists,
    /// else the employee's only billable company role — editable per row on review
    /// (decided 2026-07-23).</summary>
    private async Task<Dictionary<Guid, Guid?>> ResolveDefaultRolesAsync(
        Guid employeeId, IEnumerable<Project> mappedProjects, CancellationToken cancellationToken)
    {
        var billableRoleIds = (await roles.GetAllAsync(cancellationToken))
            .Where(r => r.IsBillable).Select(r => r.Id).ToHashSet();
        var heldBillable = (await employees.GetRoleIdsAsync(employeeId, cancellationToken))
            .Where(billableRoleIds.Contains).ToList();
        Guid? fallback = heldBillable.Count == 1 ? heldBillable[0] : null;

        var result = new Dictionary<Guid, Guid?>();
        foreach (var project in mappedProjects.DistinctBy(p => p.Id))
        {
            var assignment = await assignments.GetByProjectAndEmployeeAsync(project.Id, employeeId, cancellationToken);
            result[project.Id] = assignment?.DefaultBillingRoleId ?? fallback;
        }

        return result;
    }

    /// <summary>The employee's existing (project, date) cells in the range, excluding entries
    /// created by this same import — what upload/review flags duplicates against.</summary>
    private async Task<HashSet<(Guid ProjectId, DateOnly Date)>> GetExistingCellsAsync(
        Guid employeeId, DateOnly from, DateOnly to, Guid importId, CancellationToken cancellationToken)
    {
        var existing = await entries.GetForEmployeeRangeAsync(employeeId, from, to, cancellationToken);
        return existing
            .Where(e => e.ImportId != importId)
            .Select(e => (e.ProjectId, e.EntryDate))
            .ToHashSet();
    }

    private async Task RequirePendingAsync(Guid importId, CancellationToken cancellationToken)
    {
        var import = await imports.GetAsync(importId, cancellationToken)
            ?? throw new DomainException("Unknown import.");
        if (import.Status != TimesheetImportStatus.Pending)
        {
            throw new DomainException("This import is no longer under review.");
        }
    }

    private static bool ValidHours(decimal hours, out string problem)
    {
        problem = hours switch
        {
            <= 0 => "Hours must be greater than zero — exclude the row instead.",
            > 24 => "A single entry cannot exceed 24 hours.",
            _ when hours * 4m != Math.Truncate(hours * 4m) => "Hours must be in quarter-hour (0.25) increments.",
            _ => "",
        };
        return problem.Length == 0;
    }

    private static string Normalize(string label) => label.Trim().ToLowerInvariant();
}
