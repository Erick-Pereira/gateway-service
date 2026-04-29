using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Domain.ValueObjects;

namespace Simcag.Gateway.Application.Services;

public sealed class GatewayAccessEvaluator : IGatewayAccessEvaluator
{
    public bool IsAllowed(UserContext user, string resource, string action)
    {
        if (user.Role == Role.ADMIN)
            return true;

        if (user.Permissions is { Count: > 0 })
        {
            var code = $"{resource}:{action}";
            if (user.Permissions.Contains(code) || user.Permissions.Contains("*:*"))
                return true;
        }

        if (string.Equals(resource, "ingestion", StringComparison.OrdinalIgnoreCase))
            return user.Role is Role.SINDICO;

        if (string.Equals(resource, "admin", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(resource, "alert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(resource, "notification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(resource, "report", StringComparison.OrdinalIgnoreCase))
            return user.Role is Role.SINDICO or Role.CONSELHO;

        return false;
    }
}
