using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ino.Aspire.Hosting;
using Ino.Core;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[Collection(nameof(InoE2ECollection))]
public class InstallFlowTests(InoTestAppHost<Projects.Ino_AppHost> fixture)
{
    [Fact]
    public async Task System_silo_marketplace_endpoint_responds_with_installed_set()
    {
        var client = fixture.CreateKernelHttpClient();

        var response = await client.GetAsync("/marketplace/installed", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Install_returns_404_for_unknown_domain()
    {
        var client = fixture.CreateKernelHttpClient();

        var response = await client.PostAsync(
            "/marketplace/install/Ino.Testing.Fixture.Unknown",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// End-to-end install path: POST /marketplace/install/{id} returns 200,
    /// installed.json on disk contains the domain, and GET /marketplace/installed
    /// reflects the same set. The positive assertion the PR was missing in L5.
    /// Uninstalls at the end so the shared fixture does not carry state to
    /// other tests in the collection.
    /// </summary>
    [Fact]
    public async Task Install_then_query_then_uninstall_completes_the_round_trip()
    {
        var client = fixture.CreateKernelHttpClient();
        var ct = TestContext.Current.CancellationToken;
        const string domainId = "Ino.Testing.Fixture.Beta";

        try
        {
            var install = await client.PostAsync($"/marketplace/install/{domainId}", content: null, ct);
            Assert.Equal(HttpStatusCode.OK, install.StatusCode);

            // MarketplaceController.Install must persist state to installed.json.
            Assert.True(File.Exists(fixture.InstalledJsonPath));

            var onDisk = InstalledSet.Load(fixture.InstalledJsonPath);
            Assert.Contains(DomainId.From(domainId), onDisk);

            var query = await client.GetFromJsonAsync<InstalledResponse>("/marketplace/installed", ct);
            Assert.Contains(domainId, query!.Installed);
        }
        finally
        {
            await client.PostAsync($"/marketplace/uninstall/{domainId}", content: null, ct);
        }
    }

    [Fact]
    public async Task Available_feed_returns_domains_shape()
    {
        var client = fixture.CreateKernelHttpClient();

        var response = await client.GetAsync("/marketplace/available", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // feed shape must use .domains top-level key
        Assert.Contains("\"domains\"", body);
    }

    [Fact]
    public async Task GetInstalledNeurons_returns_404_for_not_installed_domain()
    {
        var client = fixture.CreateKernelHttpClient();

        var response = await client.GetAsync(
            "/marketplace/installed/Ino.Testing.Fixture.NotInstalled/neurons",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record InstalledResponse(
        [property: JsonPropertyName("installed")] string[] Installed);
}
