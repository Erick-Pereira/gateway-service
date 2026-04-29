namespace Simcag.Gateway.Domain.ValueObjects;

public sealed record AccessToken(string Token, string UserId, string UserName, Role Role, IReadOnlyCollection<string> Scopes, DateTime ExpiresAt)
{
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool HasScope(string scope) => Scopes.Contains(scope);
}
