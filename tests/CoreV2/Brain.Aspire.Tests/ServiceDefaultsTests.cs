using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ServiceDiscovery;
using Xunit;

namespace Brain.Aspire.Tests;

public sealed class ServiceDefaultsTests
{
    [Fact]
    public void Service_defaults_register_health_checks_and_service_discovery()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddServiceDefaults();
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<HealthCheckService>());
        Assert.NotNull(host.Services.GetService<ServiceEndpointResolver>());
    }
}
