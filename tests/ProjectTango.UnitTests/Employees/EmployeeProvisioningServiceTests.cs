using ProjectTango.Application.Employees;
using ProjectTango.Domain.Entities;

namespace ProjectTango.UnitTests.Employees;

public class EmployeeProvisioningServiceTests
{
    private readonly FakeEmployeeRepository _repository = new();
    private readonly EmployeeProvisioningService _service;

    public EmployeeProvisioningServiceTests() => _service = new EmployeeProvisioningService(_repository);

    [Fact]
    public async Task Returns_existing_employee_when_oid_already_linked()
    {
        var existing = NewEmployee(entraOid: "oid-1", email: "don@thegeospatialgroup.com");
        _repository.Employees.Add(existing);

        var result = await _service.ProvisionSignInAsync("oid-1", "don@thegeospatialgroup.com", "Don Woods");

        Assert.Equal(existing.Id, result.Id);
        Assert.Empty(_repository.Added);
    }

    [Fact]
    public async Task Links_oid_when_record_exists_by_email_only()
    {
        var seeded = NewEmployee(entraOid: null, email: "don@thegeospatialgroup.com");
        _repository.Employees.Add(seeded);

        var result = await _service.ProvisionSignInAsync("oid-new", "DON@thegeospatialgroup.com", "Don Woods");

        Assert.Equal(seeded.Id, result.Id);
        Assert.Equal("oid-new", result.EntraOid);
        Assert.Equal((seeded.Id, "oid-new"), Assert.Single(_repository.Linked));
        Assert.Empty(_repository.Added);
    }

    [Fact]
    public async Task Creates_new_employee_with_no_roles_for_unknown_user()
    {
        var result = await _service.ProvisionSignInAsync("oid-x", "newhire@thegeospatialgroup.com", "New Hire");

        var added = Assert.Single(_repository.Added);
        Assert.Equal(result.Id, added.Id);
        Assert.Equal("oid-x", added.EntraOid);
        Assert.Equal("newhire@thegeospatialgroup.com", added.Email);
        Assert.Equal("New Hire", added.DisplayName);
        Assert.True(added.IsActive);
        Assert.Empty(await _repository.GetRoleNamesAsync(added.Id));
    }

    private static Employee NewEmployee(string? entraOid, string email) => new()
    {
        Id = Guid.NewGuid(),
        EntraOid = entraOid,
        Email = email,
        DisplayName = "Test User",
    };

    private sealed class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = [];
        public List<Employee> Added { get; } = [];
        public List<(Guid EmployeeId, string EntraOid)> Linked { get; } = [];

        public Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.EntraOid == entraOid));

        public Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e =>
                string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default)
        {
            Linked.Add((employeeId, entraOid));
            return Task.CompletedTask;
        }

        public Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
        {
            Employees.Add(employee);
            Added.Add(employee);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
