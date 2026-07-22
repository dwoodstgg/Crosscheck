using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Holidays;

public interface ICompanyHolidayRepository
{
    /// <summary>Live (not soft-deleted) holidays with dates in [from, to], ordered by date.</summary>
    Task<IReadOnlyList<CompanyHoliday>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>A live holiday by id; null when unknown or already deleted.</summary>
    Task<CompanyHoliday?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The live holiday on a date, if any.</summary>
    Task<CompanyHoliday?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task AddAsync(CompanyHoliday holiday, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, Guid? deletedBy, CancellationToken cancellationToken = default);
}
