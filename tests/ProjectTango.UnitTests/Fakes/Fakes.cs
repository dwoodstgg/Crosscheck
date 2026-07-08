using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Roles;
using ProjectTango.Domain.Entities;

namespace ProjectTango.UnitTests.Fakes;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? EmployeeId { get; set; } = Guid.NewGuid();
    public HashSet<string> Roles { get; } = [];

    public bool IsInRole(string roleName) => Roles.Contains(roleName);
}

public sealed class FakeAuditLog : IAuditLog
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

public sealed class FakeRoleRepository : IRoleRepository
{
    public List<Role> Roles { get; } = [];

    public Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Role>>(Roles.ToList());

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Roles.FirstOrDefault(r => r.Id == id));
}

public sealed class FakeEmployeeRepository(FakeRoleRepository roles) : IEmployeeRepository
{
    public List<Employee> Employees { get; } = [];
    public List<Employee> Added { get; } = [];
    public List<(Guid EmployeeId, string EntraOid)> Linked { get; } = [];
    public Dictionary<Guid, HashSet<Guid>> RoleIdsByEmployee { get; } = [];

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.Id == id));

    public Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.EntraOid == entraOid));

    public Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e =>
            string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase)));

    public async Task<IReadOnlyList<EmployeeSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<EmployeeSummary>();
        foreach (var employee in Employees)
        {
            summaries.Add(new EmployeeSummary(employee, await GetRoleNamesAsync(employee.Id, cancellationToken)));
        }

        return summaries;
    }

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

    public Task SetActiveAsync(Guid employeeId, bool isActive, CancellationToken cancellationToken = default)
    {
        Employees.Single(e => e.Id == employeeId).IsActive = isActive;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var roleIds = RoleIdsByEmployee.GetValueOrDefault(employeeId, []);
        IReadOnlyList<string> names = roles.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .OrderBy(n => n)
            .ToList();
        return Task.FromResult(names);
    }

    public Task<bool> GrantRoleAsync(Guid employeeId, Guid roleId, Guid grantedBy, CancellationToken cancellationToken = default)
    {
        var set = RoleIdsByEmployee.TryGetValue(employeeId, out var existing)
            ? existing
            : RoleIdsByEmployee[employeeId] = [];
        return Task.FromResult(set.Add(roleId));
    }

    public Task<bool> RevokeRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(RoleIdsByEmployee.GetValueOrDefault(employeeId, []).Remove(roleId));

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
    {
        var adminRoleIds = roles.Roles.Where(r => r.IsSystemAdmin).Select(r => r.Id).ToHashSet();
        var count = Employees.Count(e =>
            e.IsActive && RoleIdsByEmployee.GetValueOrDefault(e.Id, []).Overlaps(adminRoleIds));
        return Task.FromResult(count);
    }
}
