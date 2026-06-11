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
        {
            // Segregação de Funções (SoD): Administrador não pode aprovar compras.
            if (resource == GatewayAccessResources.Compras && action == GatewayAccessActions.Approve)
                return false;

            return true;
        }

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

        if (string.Equals(resource, GatewayAccessResources.Notification, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(action, GatewayAccessActions.Manage, StringComparison.OrdinalIgnoreCase))
                return false;

            return user.Role is Role.SINDICO or Role.CONSELHO;
        }

        if (string.Equals(resource, GatewayAccessResources.Alert, StringComparison.OrdinalIgnoreCase))
        {
            // Segregação de Funções: Síndico/Conselho apenas visualiza (Read/Approve), Admin gerencia (Manage).
            if (string.Equals(action, GatewayAccessActions.Manage, StringComparison.OrdinalIgnoreCase))
                return user.Role is Role.ADMIN;

            return user.Role is Role.SINDICO or Role.CONSELHO;
        }

        if (string.Equals(resource, GatewayAccessResources.Report, StringComparison.OrdinalIgnoreCase))
        {
            // Dashboard Differentiation: Morador vê apenas overview, outros veem full.
            if (string.Equals(action, "view_full", StringComparison.OrdinalIgnoreCase))
                return user.Role is Role.SINDICO or Role.ADMIN;

            return true;
        }

        return false;
    }
}
