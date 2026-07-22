using Dapper;
using Npgsql;
using Crosscheck.Application.Holidays;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class CompanyHolidayRepository(NpgsqlDataSource dataSource) : ICompanyHolidayRepository
{
    private const string SelectColumns =
        """
        SELECT id, holiday_date, name, created_by, created_at
        FROM company_holidays
        """;

    public async Task<IReadOnlyList<CompanyHoliday>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<HolidayRow>(new CommandDefinition(
            $"{SelectColumns} WHERE deleted_at IS NULL AND holiday_date BETWEEN @from AND @to ORDER BY holiday_date",
            new { from, to },
            cancellationToken: cancellationToken));
        return rows.Select(ToEntity).ToList();
    }

    public async Task<CompanyHoliday?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<HolidayRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @id AND deleted_at IS NULL",
            new { id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<CompanyHoliday?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<HolidayRow>(new CommandDefinition(
            $"{SelectColumns} WHERE holiday_date = @date AND deleted_at IS NULL",
            new { date },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task AddAsync(CompanyHoliday holiday, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO company_holidays (id, holiday_date, name, created_by, created_at)
            VALUES (@Id, @Date, @Name, @CreatedById, @CreatedAt)
            """,
            new { holiday.Id, holiday.Date, holiday.Name, holiday.CreatedById, holiday.CreatedAt },
            cancellationToken: cancellationToken));
    }

    public async Task SoftDeleteAsync(Guid id, Guid? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE company_holidays SET deleted_at = now(), deleted_by = @deletedBy
            WHERE id = @id AND deleted_at IS NULL
            """,
            new { id, deletedBy },
            cancellationToken: cancellationToken));
    }

    private static CompanyHoliday ToEntity(HolidayRow row) => new()
    {
        Id = row.Id,
        Date = row.HolidayDate,
        Name = row.Name,
        CreatedById = row.CreatedBy,
        CreatedAt = row.CreatedAt,
    };

    private sealed class HolidayRow
    {
        public Guid Id { get; set; }
        public DateOnly HolidayDate { get; set; }
        public string Name { get; set; } = "";
        public Guid? CreatedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
