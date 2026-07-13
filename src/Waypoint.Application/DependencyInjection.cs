using Microsoft.Extensions.DependencyInjection;
using Waypoint.Application.Clients;
using Waypoint.Application.Employees;
using Waypoint.Application.Preferences;
using Waypoint.Application.Projects;
using Waypoint.Application.Roles;
using Waypoint.Application.TimeEntries;

namespace Waypoint.Application;

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
