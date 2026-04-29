using Simcag.Gateway.Domain.Entities;

namespace Simcag.Gateway.Application.Interfaces;

/// <summary>Autorização do gateway: recurso/ação, não regra de negócio do condomínio.</summary>
public interface IGatewayAccessEvaluator
{
    bool IsAllowed(UserContext user, string resource, string action);
}
