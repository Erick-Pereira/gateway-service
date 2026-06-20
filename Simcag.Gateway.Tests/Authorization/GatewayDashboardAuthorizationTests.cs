using FluentAssertions;
using Simcag.Gateway.Application.Authorization;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Application.Services;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Domain.ValueObjects;

namespace Simcag.Gateway.Tests.Authorization;

public sealed class GatewayAuthorizationPathCatalogDashboardTests
{
    [Theory]
    [InlineData("/api/dashboard/summary", GatewayAccessActions.Read)]
    [InlineData("/api/dashboard/monthly", GatewayAccessActions.Read)]
    [InlineData("/api/dashboard/full", GatewayAccessActions.view_full)]
    public void TryResolveResourceAction_Dashboard_MapsExpectedAction(string path, string expectedAction)
    {
        var ok = GatewayAuthorizationPathCatalog.TryResolveResourceAction(path, "GET", out var resource, out var action);

        ok.Should().BeTrue();
        resource.Should().Be(GatewayAccessResources.Dashboard);
        action.Should().Be(expectedAction);
    }
}

public sealed class GatewayAccessEvaluatorDashboardTests
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
    [InlineData(Role.SINDICO, GatewayAccessActions.view_full, true)]
    [InlineData(Role.CONSELHO, GatewayAccessActions.Read, true)]
    [InlineData(Role.CONSELHO, GatewayAccessActions.view_full, true)]
    [InlineData(Role.ADMIN, GatewayAccessActions.Read, true)]
    [InlineData(Role.ADMIN, GatewayAccessActions.view_full, true)]
    [InlineData(Role.MORADOR, GatewayAccessActions.Read, false)]
    [InlineData(Role.MORADOR, GatewayAccessActions.view_full, false)]
    public void IsAllowed_Dashboard_GrantsSindicoConselhoNotMorador(Role role, string action, bool expected)
    {
        _evaluator.IsAllowed(User(role), GatewayAccessResources.Dashboard, action).Should().Be(expected);
    }
}
