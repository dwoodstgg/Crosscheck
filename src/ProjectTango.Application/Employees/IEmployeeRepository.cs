using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default);

    Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default);

    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
