using Microsoft.Extensions.DependencyInjection;
using Crosscheck.Application.Clients;
using Crosscheck.Application.Employees;
using Crosscheck.Application.Preferences;
using Crosscheck.Application.Projects;
using Crosscheck.Application.Roles;
using Crosscheck.Application.TimeEntries;

namespace Crosscheck.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<EmployeeProvisioningService>();
        services.AddScoped<EmployeeAdminService>();
        services.AddScoped<RoleAdminService>();
        services.AddScoped<ClientAdminService>();
        services.AddScoped<ProjectAdminService>();
        services.AddScoped<RateCardService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<ModuleService>();
        services.AddScoped<IBudgetAlertService, BudgetAlertService>();
        services.AddScoped<ProjectDashboardService>();
        services.AddScoped<TimesheetService>();
        services.AddScoped<TimeEntryService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<TimesheetPeriodService>();
        services.AddScoped<PreferenceService>();
        return services;
    }
}
