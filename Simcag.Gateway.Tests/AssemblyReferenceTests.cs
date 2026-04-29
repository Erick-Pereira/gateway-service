using Simcag.Gateway.Infrastructure.Configuration;
using Xunit;

namespace Simcag.Gateway.Tests;

public class AssemblyReferenceTests
{
    [Fact]
    public void Infrastructure_diagnostics_registration_exists() =>
        Assert.NotNull(typeof(DependencyInjection));
}
