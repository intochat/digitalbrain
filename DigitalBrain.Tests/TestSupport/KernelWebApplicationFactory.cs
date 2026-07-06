using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.TestSupport;

/// <summary>
/// WebApplicationFactory for the kernel that forces test mode (no neuron warmup, no MCP spawn in SystemStatus).
/// Used by gateway and kernel gRPC surface contract tests so they exercise the host without side effects or timeouts.
/// </summary>
public sealed class KernelWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:TestMode"] = "true",
                ["DIGITALBRAIN_TEST_MODE"] = "true"
            });
        });
    }
}
