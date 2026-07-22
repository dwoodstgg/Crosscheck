using Crosscheck.Application.Common;

namespace Crosscheck.Web.Services;

/// <summary>Adapts the signed-in ClaimsPrincipal (cookie or JWT bearer) to the
/// service layer's ICurrentUser.</summary>
public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? EmployeeId => httpContextAccessor.HttpContext?.User.GetEmployeeId();

    public bool IsInRole(string roleName) =>
        httpContextAccessor.HttpContext?.User.IsInRole(roleName) ?? false;
}
