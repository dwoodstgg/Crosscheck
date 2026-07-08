using Dapper;
using Npgsql;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class TimesheetPeriodRepository(NpgsqlDataSource dataSource) : ITimesheetPeriodRepository
{
    private const string SelectColumns =
        """
        SELECT id, period_start, period_end, status, closed_by, closed_at
        FROM timesheet_periods
        """;

    public async Task<TimesheetPeriod?> GetByStartAsync(DateOnly periodStart, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PeriodRow>(new CommandDefinition(
            $"{SelectColumns} WHERE period_start = @periodStart",
            new { periodStart },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<TimesheetPeriod>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<PeriodRow>(new CommandDefinition(
            $"{SelectColumns} WHERE period_start <= @to AND period_end >= @from ORDER BY period_start",
            new { from, to },
            cancellationToken: cancellationToken));
        return rows.Select(ToEntity).ToList();
    }

    public async Task UpsertAsync(TimesheetPeriod period, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO timesheet_periods (id, period_start, period_end, status, closed_by, closed_at)
            VALUES (@Id, @PeriodStart, @PeriodEnd, @Status, @ClosedById, @ClosedAt)
            ON CONFLICT (period_start) DO UPDATE SET
                status = EXCLUDED.status,
                closed_by = EXCLUDED.closed_by,
                closed_at = EXCLUDED.closed_at
            """,
            new
            {
                period.Id,
                period.PeriodStart,
                period.PeriodEnd,
                Status = DbEnum.ToDb(period.Status),
                period.ClosedById,
                period.ClosedAt,
            },
            cancellationToken: cancellationToken));
    }

    private static TimesheetPeriod ToEntity(PeriodRow row) => new()
    {
        Id = row.Id,
        PeriodStart = row.PeriodStart,
        PeriodEnd = row.PeriodEnd,
        Status = DbEnum.FromDb<TimesheetPeriodStatus>(row.Status),
        ClosedById = row.ClosedBy,
        ClosedAt = row.ClosedAt,
    };

    private sealed class PeriodRow
    {
        public Guid Id { get; set; }
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public string Status { get; set; } = "open";
        public Guid? ClosedBy { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
    }
}
