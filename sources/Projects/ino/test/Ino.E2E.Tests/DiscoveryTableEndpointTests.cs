using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[Collection(nameof(InoE2ECollection))]
public class DiscoveryTableEndpointTests(InoTestAppHost<Projects.Ino_AppHost> fixture)
{
    [Fact]
    public async Task Table_endpoint_is_routed_on_system_silo()
    {
        var client = fixture.CreateKernelHttpClient();

        var response = await client.GetAsync("/discovery/table", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dump = await response.Content.ReadFromJsonAsync<DiscoveryDumpResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(dump);
        Assert.NotNull(dump!.Canonical);
        Assert.NotNull(dump.Reactive);
        Assert.NotNull(dump.CountsBySilo);

        // Cross-silo registration check: SystemEcho comes from the system silo's
        // built-in registration, FlightSearch comes from the domains
        // silo's Travel install. Both landing in the same dump proves Discovery
        // is a single cluster-wide activation — the [PinToSilo] placement fix
        // (Ino.Core.Hosting.Placement.PinToSiloAttribute). Without the pin,
        // default random placement produced one activation per silo under
        // parallel startup, so the reader on system silo would see only its
        // own write.
        // system silo registers SystemEcho at startup
        Assert.Contains(dump.Canonical.Select(c => c.GrainType), gt => gt!.EndsWith(".SystemEcho"));
        // domains silo registers FlightSearch via the Travel domain — absence means the cross-silo registration call never reached the system-silo Discovery activation
        Assert.Contains(dump.Canonical.Select(c => c.GrainType), gt => gt!.EndsWith(".FlightSearch"));
        Assert.True(dump.CountsBySilo.ContainsKey("system"));
        Assert.True(dump.CountsBySilo.ContainsKey("domains"));
    }

    private sealed record DiscoveryDumpResponse(
        [property: JsonPropertyName("canonical")] CanonicalDumpEntry[] Canonical,
        [property: JsonPropertyName("reactive")] ReactiveDumpEntry[] Reactive,
        [property: JsonPropertyName("countsBySilo")] Dictionary<string, int> CountsBySilo);

    private sealed record CanonicalDumpEntry(
        [property: JsonPropertyName("grainType")] string GrainType);

    private sealed record ReactiveDumpEntry(
        [property: JsonPropertyName("grainType")] string GrainType);
}
