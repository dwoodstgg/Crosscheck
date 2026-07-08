using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Employees;

/// <summary>Resolves an Entra sign-in to a local employee record (design-doc.md §4.2).
/// The Entra oid is the stable key; email is the bootstrap key that links records
/// created ahead of time (seed, imports, manual provisioning) on their first sign-in.</summary>
public class EmployeeProvisioningService(IEmployeeRepository employees)
{
    public async Task<Employee> ProvisionSignInAsync(
        string entraOid, string email, string displayName, CancellationToken cancellationToken = default)
    {
        var existing = await employees.GetByEntraOidAsync(entraOid, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var byEmail = await employees.GetByEmailAsync(email, cancellationToken);
        if (byEmail is not null)
        {
            await employees.LinkEntraOidAsync(byEmail.Id, entraOid, cancellationToken);
            byEmail.EntraOid = entraOid;
            return byEmail;
        }

        // Unknown tenant user: create the record, but with NO roles — an Admin or
        // Operations Manager must grant access before they can do anything.
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EntraOid = entraOid,
            Email = email,
            DisplayName = displayName,
        };
        await employees.AddAsync(employee, cancellationToken);
        return employee;
    }
}
