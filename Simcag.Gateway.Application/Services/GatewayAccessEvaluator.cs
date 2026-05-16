using Simcag.Gateway.Application.Authorization;
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

        if (string.Equals(resource, GatewayAccessResources.Ingestion, StringComparison.OrdinalIgnoreCase))
            return user.Role is Role.SINDICO;

        if (string.Equals(resource, GatewayAccessResources.Admin, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(resource, GatewayAccessResources.Alert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resource, GatewayAccessResources.Notification, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resource, GatewayAccessResources.Report, StringComparison.OrdinalIgnoreCase))
            return user.Role is Role.SINDICO or Role.CONSELHO;

        return false;
    }
}
