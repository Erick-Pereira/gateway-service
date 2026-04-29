using Simcag.Gateway.Domain.ValueObjects;

namespace Simcag.Gateway.Domain.Entities;

public sealed class UserContext
{
    public string UserId { get; }
    public string UserName { get; }
    public string TenantId { get; }
    public Role Role { get; }
    public IReadOnlyCollection<string> Permissions { get; }
    public AccessToken Token { get; }

    public UserContext(
        string userId,
        string userName,
        string tenantId,
        Role role,
        IReadOnlyCollection<string> permissions,
        AccessToken token)
    {
        UserId = userId;
        UserName = userName;
        TenantId = tenantId;
        Role = role;
        Permissions = permissions;
        Token = token;
    }

    public bool CanAccess(string resource, string action)
    {
        if (Role == Role.ADMIN) return true;

        var permissionCode = $"{resource}:{action}";
        return Permissions.Contains(permissionCode) || Permissions.Contains("*:*");
    }
}
