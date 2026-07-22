using Crosscheck.Application.Holidays;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Holidays;

public class HolidayServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeCompanyHolidayRepository _holidays = new();
    private readonly FakeAuditLog _audit = new();
    private readonly HolidayService _service;

    public HolidayServiceTests()
    {
        _service = new HolidayService(_currentUser, _holidays, _audit);
        _currentUser.Roles.Add(RoleNames.Admin);
    }

    [Fact]
    public async Task Add_stores_the_holiday_and_audits()
    {
        var holiday = await _service.AddAsync(new DateOnly(2026, 12, 25), "  Christmas Day  ");

        var stored = Assert.Single(_holidays.Holidays);
        Assert.Equal(new DateOnly(2026, 12, 25), stored.Date);
        Assert.Equal("Christmas Day", stored.Name);
        Assert.Equal(_currentUser.EmployeeId, stored.CreatedById);
        Assert.Equal(holiday.Id, stored.Id);
        Assert.Single(_audit.Events, e => e.Action == "company_holiday.created");
    }

    [Fact]
    public async Task Add_rejects_an_empty_name()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.AddAsync(new DateOnly(2026, 12, 25), "   "));

        Assert.Empty(_holidays.Holidays);
    }

    [Fact]
    public async Task Add_rejects_a_duplicate_date()
    {
        await _service.AddAsync(new DateOnly(2026, 12, 25), "Christmas Day");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.AddAsync(new DateOnly(2026, 12, 25), "Xmas"));

        Assert.Contains("Christmas Day", ex.Message);
        Assert.Single(_holidays.Holidays);
    }

    [Fact]
    public async Task A_non_admin_cannot_mutate_the_calendar()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.OperationsManager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.AddAsync(new DateOnly(2026, 12, 25), "Christmas Day"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RemoveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Anyone_can_read_the_calendar()
    {
        _currentUser.Roles.Clear();
        _holidays.Holidays.Add(new CompanyHoliday { Id = Guid.NewGuid(), Date = new DateOnly(2026, 7, 3), Name = "Independence Day (observed)" });

        var year = await _service.ListYearAsync(2026);
        var range = await _service.ListInRangeAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        Assert.Single(year);
        Assert.Single(range);
    }

    [Fact]
    public async Task Remove_soft_deletes_and_audits()
    {
        var holiday = await _service.AddAsync(new DateOnly(2026, 12, 25), "Christmas Day");

        await _service.RemoveAsync(holiday.Id);

        Assert.Empty(_holidays.Holidays);
        var deleted = Assert.Single(_holidays.Deleted);
        Assert.Equal(holiday.Id, deleted.Id);
        Assert.Equal(_currentUser.EmployeeId, deleted.DeletedBy);
        Assert.Single(_audit.Events, e => e.Action == "company_holiday.deleted");
    }

    [Fact]
    public async Task Removing_an_unknown_holiday_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.RemoveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Copy_recomputes_floating_holidays_and_audits_each_creation()
    {
        await _service.AddAsync(new DateOnly(2026, 1, 19), "Martin Luther King Day"); // 3rd Mon
        await _service.AddAsync(new DateOnly(2026, 12, 25), "Christmas");             // fixed
        _audit.Events.Clear();

        var result = await _service.CopyFromPreviousYearAsync(2027);

        Assert.Equal(new CopyYearResult(Added: 2, Skipped: 0), result);
        var copied = _holidays.Holidays.Where(h => h.Date.Year == 2027).OrderBy(h => h.Date).ToList();
        Assert.Equal(new DateOnly(2027, 1, 18), copied[0].Date);
        Assert.Equal("Martin Luther King Day", copied[0].Name);
        Assert.Equal(new DateOnly(2027, 12, 25), copied[1].Date);
        Assert.Equal(2, _audit.Events.Count(e => e.Action == "company_holiday.created"));
    }

    [Fact]
    public async Task Copy_skips_dates_already_on_the_target_year()
    {
        await _service.AddAsync(new DateOnly(2026, 12, 25), "Christmas");
        await _service.AddAsync(new DateOnly(2027, 12, 25), "Christmas");

        var result = await _service.CopyFromPreviousYearAsync(2027);

        Assert.Equal(new CopyYearResult(Added: 0, Skipped: 1), result);
        Assert.Equal(2, _holidays.Holidays.Count);
    }

    [Fact]
    public async Task Copying_an_empty_year_adds_nothing()
    {
        Assert.Equal(new CopyYearResult(0, 0), await _service.CopyFromPreviousYearAsync(2027));
        Assert.Empty(_holidays.Holidays);
    }

    [Fact]
    public async Task A_non_admin_cannot_copy_the_calendar()
    {
        _currentUser.Roles.Clear();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CopyFromPreviousYearAsync(2027));
    }
}
