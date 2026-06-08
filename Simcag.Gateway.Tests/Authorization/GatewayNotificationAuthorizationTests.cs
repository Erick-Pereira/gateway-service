using FluentAssertions;
using Simcag.Gateway.Application.Authorization;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Application.Services;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Domain.ValueObjects;

namespace Simcag.Gateway.Tests.Authorization;

public sealed class GatewayAuthorizationPathCatalogTests
{
    [Theory]
    [InlineData("/api/notifications/preferences/abc", "GET", GatewayAccessActions.Read)]
    [InlineData("/api/notifications/preferences", "PUT", GatewayAccessActions.Write)]
    [InlineData("/api/notifications/operational/dashboard", "GET", GatewayAccessActions.Manage)]
    [InlineData("/api/notifications/deliveries", "GET", GatewayAccessActions.Manage)]
    [InlineData("/api/notifications/governance", "GET", GatewayAccessActions.Read)]
    public void TryResolveResourceAction_Notifications_MapsExpectedAction(string path, string method, string expectedAction)
    {
        var ok = GatewayAuthorizationPathCatalog.TryResolveResourceAction(path, method, out var resource, out var action);

        ok.Should().BeTrue();
        resource.Should().Be(GatewayAccessResources.Notification);
        action.Should().Be(expectedAction);
    }
}

public sealed class GatewayAccessEvaluatorNotificationTests
{
    private readonly IGatewayAccessEvaluator _evaluator = new GatewayAccessEvaluator();

    private static UserContext User(Role role)
    {
        var id = Guid.NewGuid().ToString();
        var token = new AccessToken("test", id, "Test", role, [], DateTime.UtcNow.AddHours(1));
        return new UserContext(id, "Test", Guid.NewGuid().ToString(), role, [], token);
    }

    [Theory]
    [InlineData(Role.SINDICO, GatewayAccessActions.Read, true)]
    [InlineData(Role.SINDICO, GatewayAccessActions.Write, true)]
    [InlineData(Role.SINDICO, GatewayAccessActions.Manage, false)]
    [InlineData(Role.CONSELHO, GatewayAccessActions.Write, true)]
    [InlineData(Role.MORADOR, GatewayAccessActions.Read, false)]
    [InlineData(Role.ADMIN, GatewayAccessActions.Manage, true)]
    public void IsAllowed_Notification_SplitsOperationalAndPreferences(Role role, string action, bool expected)
    {
        _evaluator.IsAllowed(User(role), GatewayAccessResources.Notification, action).Should().Be(expected);
    }
}
