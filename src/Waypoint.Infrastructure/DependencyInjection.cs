using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Waypoint.Application.Clients;
using Waypoint.Application.Common;
using Waypoint.Application.Employees;
using Waypoint.Application.Preferences;
using Waypoint.Application.Projects;
using Waypoint.Application.Roles;
using Waypoint.Application.TimeEntries;
using Waypoint.Infrastructure.Email;
using Waypoint.Infrastructure.Persistence;
using Waypoint.Infrastructure.Persistence.Repositories;

namespace Waypoint.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperConfig.Apply();

        var connectionString = configuration.GetConnectionString("Waypoint")
            ?? throw new InvalidOperationException("Connection string 'Waypoint' is missing.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IRateCardRepository, RateCardRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IBudgetAlertRepository, BudgetAlertRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ITimesheetPeriodRepository, TimesheetPeriodRepository>();
        services.AddScoped<IEmployeePreferenceRepository, EmployeePreferenceRepository>();
        services.AddScoped<IAuditLog, AuditLogRepository>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
