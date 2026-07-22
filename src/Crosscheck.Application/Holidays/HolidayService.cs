using Crosscheck.Application.Common;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Holidays;

/// <summary>The company holiday calendar. Reads are open to any signed-in user — every
/// timesheet needs the calendar to grey out holidays and compute expected workdays.
/// Mutations are Admin-only (design: "admin-managed holiday calendar") and audited.</summary>
public class HolidayService(ICurrentUser currentUser, ICompanyHolidayRepository holidays, IAuditLog audit)
{
    public Task<IReadOnlyList<CompanyHoliday>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        holidays.GetInRangeAsync(from, to, cancellationToken);

    public Task<IReadOnlyList<CompanyHoliday>> ListYearAsync(int year, CancellationToken cancellationToken = default) =>
        holidays.GetInRangeAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), cancellationToken);

    public async Task<CompanyHoliday> AddAsync(DateOnly date, string name, CancellationToken cancellationToken = default)
    {
        RequireAdmin();

        name = name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw new DomainException("The holiday needs a name.");
        }

        if (await holidays.GetByDateAsync(date, cancellationToken) is { } existing)
        {
            throw new DomainException($"{date:MMMM d, yyyy} is already a holiday (\"{existing.Name}\").");
        }

        return await CreateAsync(date, name, cancellationToken);
    }

    /// <summary>Copies the previous year's holidays into <paramref name="year"/>, recomputing
    /// floating federal holidays by their nth-weekday rule (see <see cref="HolidayRecurrence"/>).
    /// Dates already on the target year's calendar are skipped, so copying is safe to repeat
    /// and the copied year stays fully editable.</summary>
    public async Task<CopyYearResult> CopyFromPreviousYearAsync(int year, CancellationToken cancellationToken = default)
    {
        RequireAdmin();

        var added = 0;
        var skipped = 0;
        foreach (var source in await ListYearAsync(year - 1, cancellationToken))
        {
            var date = HolidayRecurrence.ForYear(source.Name, source.Date, year);
            if (await holidays.GetByDateAsync(date, cancellationToken) is not null)
            {
                skipped++;
                continue;
            }

            await CreateAsync(date, source.Name, cancellationToken);
            added++;
        }

        return new CopyYearResult(added, skipped);
    }

    private async Task<CompanyHoliday> CreateAsync(DateOnly date, string name, CancellationToken cancellationToken)
    {
        var holiday = new CompanyHoliday
        {
            Id = Guid.NewGuid(),
            Date = date,
            Name = name,
            CreatedById = currentUser.EmployeeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await holidays.AddAsync(holiday, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "company_holiday.created", "company_holiday", holiday.Id,
            new { Date = date.ToString("yyyy-MM-dd"), Name = name }), cancellationToken);

        return holiday;
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        RequireAdmin();

        var holiday = await holidays.GetByIdAsync(id, cancellationToken)
            ?? throw new DomainException("Unknown holiday.");

        await holidays.SoftDeleteAsync(id, currentUser.EmployeeId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "company_holiday.deleted", "company_holiday", id,
            new { Date = holiday.Date.ToString("yyyy-MM-dd"), holiday.Name }), cancellationToken);
    }

    private void RequireAdmin()
    {
        if (!currentUser.IsInRole(RoleNames.Admin))
        {
            throw new UnauthorizedAccessException("Managing holidays requires the Admin role.");
        }
    }
}

/// <summary>Outcome of a "copy from previous year" run.</summary>
public record CopyYearResult(int Added, int Skipped);
