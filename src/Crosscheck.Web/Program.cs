using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Identity.Web.UI;
using Crosscheck.Application;
using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Infrastructure;
using Crosscheck.Infrastructure.Persistence;
using Crosscheck.Web;
using Crosscheck.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run -- migrate` applies pending DbUp scripts and exits (used by CI/deploy).
// In Development, Database:MigrateOnStartup=true also applies them on normal startup.
if (args.Contains("migrate"))
{
    DatabaseMigrator.MigrateToLatest(GetConnectionString(builder.Configuration));
    return;
}

// Razor UI signs in with OIDC + cookies; /api/v1 accepts Entra-issued JWT bearer
// tokens (the path future mobile/desktop clients use). Same app registration.
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"), JwtBearerDefaults.AuthenticationScheme);

// First sign-in provisioning (design-doc.md §4.2): resolve the Entra identity to an
// employee record (linking by email if one pre-exists) and stamp the cookie with the
// employee id + role claims. Role grants take effect at the next sign-in.
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // Authorization code flow + PKCE (design-doc §4.1) — not the library default
    // (implicit id_token), which Entra rejects unless enabled per-registration.
    options.ResponseType = OpenIdConnectResponseType.Code;

    var previousOnTokenValidated = options.Events.OnTokenValidated;
    options.Events.OnTokenValidated = async context =>
    {
        await previousOnTokenValidated(context);

        var principal = context.Principal!;
        var entraOid = principal.GetObjectId()
            ?? throw new InvalidOperationException("Entra token is missing the oid claim.");
        var email = principal.FindFirstValue("preferred_username")
            ?? throw new InvalidOperationException("Entra token is missing the preferred_username claim.");
        var displayName = principal.FindFirstValue("name") ?? email;

        var services = context.HttpContext.RequestServices;
        var provisioning = services.GetRequiredService<EmployeeProvisioningService>();
        var employees = services.GetRequiredService<IEmployeeRepository>();

        var employee = await provisioning.ProvisionSignInAsync(entraOid, email, displayName);
        var roleNames = await employees.GetRoleNamesAsync(employee.Id);

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(CrosscheckClaims.EmployeeId, employee.Id.ToString()));
        identity.AddClaims(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));
    };
});

builder.Services.AddAuthorization();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    DatabaseMigrator.MigrateToLatest(GetConnectionString(app.Configuration));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // serves /openapi/v1.json
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static string GetConnectionString(IConfiguration configuration) =>
    configuration.GetConnectionString("Crosscheck")
    ?? throw new InvalidOperationException("Connection string 'Crosscheck' is missing.");

public partial class Program;
