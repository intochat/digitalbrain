using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Kernel;

// Regression coverage for the task-23 "/health returns 500 under aspire run" bug. HealthEndpointTests.cs
// exercises Program.cs via WebApplicationFactory<Program>, which never sets ConnectionStrings__clustering/
// grainstate/journal, so it takes the isAspireHosted=false branch and never calls
// AddKeyedAzureTableServiceClient / AddKeyedAzureBlobServiceClient / AddAzureBlobServiceClient at all --
// the exact registrations that were at fault. This test instead replicates Program.cs's isAspireHosted=true
// Azure client wiring directly against a bare IHostApplicationBuilder. No Kestrel/Orleans/real Azurite is
// needed: the bug was a pure DI-resolution failure inside the health check's registration factory, which
// fires before any network call is attempted, so it reproduces (and stays fixed) without live storage.
public class AzureClientHealthCheckRegistrationTests
{
    [Fact]
    public async Task AspireHostedBlobServiceClientRegistrations_HealthChecksDoNotThrow()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:clustering"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:grainstate"] = "UseDevelopmentStorage=true";

        // Mirrors DigitalBrain.Kernel/Program.cs's isAspireHosted branch.
        builder.AddKeyedAzureTableServiceClient("clustering");
        builder.AddKeyedAzureBlobServiceClient("grainstate");
        builder.AddAzureBlobServiceClient("grainstate", settings => settings.DisableHealthChecks = true);

        using var host = builder.Build();
        var healthCheckService = host.Services.GetRequiredService<HealthCheckService>();

        // Before the fix, this threw InvalidOperationException ("No service for type
        // 'Azure.Storage.Blobs.BlobServiceClient' has been registered"): the unkeyed
        // AddAzureBlobServiceClient("grainstate") call shares its connection name with the keyed
        // registration above, so Aspire never adds an unkeyed BlobServiceClient to DI -- only the keyed
        // one exists -- yet it still auto-registered an unkeyed health check that resolved
        // GetRequiredService<BlobServiceClient>(). DefaultHealthCheckService only catches exceptions
        // thrown by a check's own CheckHealthAsync, not ones thrown while constructing the check, so this
        // surfaced as an unhandled 500 rather than an "Unhealthy" report entry. Azurite isn't running here,
        // so the still-enabled keyed checks may legitimately report Unhealthy from a failed connection --
        // what must not happen is an unhandled throw out of CheckHealthAsync itself.
        var report = await healthCheckService.CheckHealthAsync();

        Assert.True(report.Entries.ContainsKey("Azure_TableServiceClient_clustering"));
        Assert.True(report.Entries.ContainsKey("Azure_BlobServiceClient_grainstate"));
        Assert.False(report.Entries.ContainsKey("Azure_BlobServiceClient"), "the unkeyed blob health check must stay disabled");
    }
}
