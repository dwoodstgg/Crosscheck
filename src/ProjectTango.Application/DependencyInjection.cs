using Microsoft.Extensions.DependencyInjection;
using ProjectTango.Application.Employees;

namespace ProjectTango.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<EmployeeProvisioningService>();
        services.AddScoped<EmployeeAdminService>();
        return services;
    }
}
