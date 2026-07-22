using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Crosscheck.Application.Clients;
using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Application.Preferences;
using Crosscheck.Application.Projects;
using Crosscheck.Application.Roles;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Infrastructure.Email;
using Crosscheck.Infrastructure.Persistence;
using Crosscheck.Infrastructure.Persistence.Repositories;

namespace Crosscheck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperConfig.Apply();

        var connectionString = configuration.GetConnectionString("Crosscheck")
            ?? throw new InvalidOperationException("Connection string 'Crosscheck' is missing.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IRateCardRepository, RateCardRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();
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
