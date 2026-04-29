using Simcag.Gateway.Domain.Entities;

namespace Simcag.Gateway.Application.Interfaces;

public interface IAuthService
{
    Task<UserContext?> ValidateTokenAsync(string token);
    Task<bool> HasPermissionAsync(UserContext userContext, string resource, string action);
}
