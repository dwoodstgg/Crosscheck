using System.Security.Claims;

namespace ProjectTango.Web;

/// <summary>App-specific claims stamped onto the auth cookie at sign-in.</summary>
public static class TangoClaims
{
    public const string EmployeeId = "tango:employee_id";

    public static Guid? GetEmployeeId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(EmployeeId), out var id) ? id : null;
}
