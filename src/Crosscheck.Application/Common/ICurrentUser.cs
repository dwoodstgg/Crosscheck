namespace Crosscheck.Application.Common;

/// <summary>The signed-in actor, as seen by the service layer. Authorization is enforced
/// here (design rule: never only in views/controllers).</summary>
public interface ICurrentUser
{
    Guid? EmployeeId { get; }

    bool IsInRole(string roleName);
}
