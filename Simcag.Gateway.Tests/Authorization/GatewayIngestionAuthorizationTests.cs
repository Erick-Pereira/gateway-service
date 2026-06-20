using FluentAssertions;
using Simcag.Gateway.Application.Authorization;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Application.Services;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Domain.ValueObjects;

namespace Simcag.Gateway.Tests.Authorization;

public sealed class GatewayAuthorizationPathCatalogIngestionTests
{
    [Theory]
    [InlineData("/api/ingestion/upload", "POST", GatewayAccessActions.Write)]
    [InlineData("/api/ingestion/documents/abc", "GET", GatewayAccessActions.Read)]
    public void TryResolveResourceAction_Ingestion_MapsExpectedAction(string path, string method, string expectedAction)
    {
        var ok = GatewayAuthorizationPathCatalog.TryResolveResourceAction(path, method, out var resource, out var action);

        ok.Should().BeTrue();
        resource.Should().Be(GatewayAccessResources.Ingestion);
        action.Should().Be(expectedAction);
    }
}

public sealed class GatewayAccessEvaluatorIngestionTests
{
    private readonly IGatewayAccessEvaluator _evaluator = new GatewayAccessEvaluator();

    private static UserContext User(Role role)
    {
        var id = Guid.NewGuid().ToString();
        var token = new AccessToken("test", id, "Test", role, [], DateTime.UtcNow.AddHours(1));
        return new UserContext(id, "Test", Guid.NewGuid().ToString(), role, [], token);
    }

    [Theory]
    [InlineData(Role.SINDICO, GatewayAccessActions.Write, true)]
    [InlineData(Role.SINDICO, GatewayAccessActions.Read, true)]
    [InlineData(Role.CONSELHO, GatewayAccessActions.Write, false)]
    [InlineData(Role.CONSELHO, GatewayAccessActions.Read, true)]
    [InlineData(Role.ADMIN, GatewayAccessActions.Write, true)]
    [InlineData(Role.MORADOR, GatewayAccessActions.Write, false)]
    [InlineData(Role.MORADOR, GatewayAccessActions.Read, false)]
    public void IsAllowed_Ingestion_SindicoUploadsConselhoReadOnly(Role role, string action, bool expected)
    {
        _evaluator.IsAllowed(User(role), GatewayAccessResources.Ingestion, action).Should().Be(expected);
    }
}
