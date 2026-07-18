using System.Security.Claims;
using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Flutter;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public class UiGatewayTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity(
        [
            new Claim("digitalbrain:owner", "local-owner"),
            new Claim("digitalbrain:space", "actor/ui-dev"),
            new Claim("digitalbrain:grant", "test.echo.v1")
        ],
        "test"));

    private static string UiTestAddress(string id) =>
        new NeuronAddress("local-owner", "actor/ui-dev", $"test/{id}").ToGrainKey();

    [Fact]
    public async Task Invoke_typed_contract_returns_receipt_at_revision_one()
    {
        var address = UiTestAddress(Guid.NewGuid().ToString("N"));
        var receipt = await UiEndpoints.InvokeAsync(
            Cluster.Client,
            Principal,
            new FlutterGatewayPolicy(),
            address,
            "test.echo.v1",
            """{"text":"hello"}""",
            "cmd-1",
            null);

        Assert.Equal(1, receipt.Revision);
        Assert.Equal("cmd-1", receipt.CommandId);
    }

    [Fact]
    public async Task Invoke_rejects_duplicate_mutation_command_id()
    {
        var address = UiTestAddress(Guid.NewGuid().ToString("N"));
        var policy = new FlutterGatewayPolicy();
        var first = await UiEndpoints.InvokeAsync(
            Cluster.Client,
            Principal,
            policy,
            address,
            "test.echo.v1",
            """{"text":"hello"}""",
            "cmd-dup",
            null);
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            UiEndpoints.InvokeAsync(
                Cluster.Client,
                Principal,
                policy,
                address,
                "test.echo.v1",
                """{"text":"ignored"}""",
                "cmd-dup",
                null));

        Assert.Equal(1, first.Revision);
        Assert.Equal("command.replayed", exception.Code);
    }

    [Fact]
    public async Task Read_and_describe_pass_through_to_the_neuron()
    {
        var address = new NeuronAddress(
            "local-owner",
            "actor/ui-dev",
            "catalog/main").ToGrainKey();
        var policy = new FlutterGatewayPolicy();

        var snapshot = await UiEndpoints.ReadAsync(
            Cluster.Client,
            Principal,
            policy,
            address,
            "");
        Assert.Contains("\"kind\":\"test\"", snapshot.StateJson);

        var description = await UiEndpoints.DescribeAsync(
            Cluster.Client,
            Principal,
            policy,
            address);
        Assert.Equal("catalog", description.Kind);
    }

    [Fact]
    public void ToErrorPayload_maps_brain_exception_to_code_and_detail()
    {
        var exception = new BrainException(
            BrainErrors.UnknownContract,
            "bad contract");
        var json = JsonSerializer.Serialize(UiEndpoints.ToErrorPayload(exception));

        Assert.Contains("\"code\":\"contract.unknown\"", json);
        Assert.Contains("\"detail\":\"bad contract\"", json);
    }

    [Fact]
    public void WatchPager_maps_feed_records_skips_other_events_and_advances_cursor()
    {
        var page = new NeuronEventPage(
            [
                new NeuronEvent(
                    1,
                    "feed.record",
                    """{"sourceKey":"owner|actor/test|test/x","revision":1,"kind":"test"}""",
                    "cmd-1",
                    DateTimeOffset.UtcNow),
                new NeuronEvent(
                    2,
                    "echoed",
                    """{"text":"not a feed record"}""",
                    "cmd-2",
                    DateTimeOffset.UtcNow),
                new NeuronEvent(
                    3,
                    "feed.record",
                    """{"sourceKey":"owner|actor/test|test/y","revision":2,"kind":"test"}""",
                    "cmd-3",
                    DateTimeOffset.UtcNow)
            ],
            NextRevision: 3);

        var frames = WatchPager.NextFrames(page);

        Assert.Equal(2, frames.Count);
        Assert.Contains("\"sequence\":1", frames[0]);
        Assert.Contains("test/x", frames[0]);
        Assert.Contains("\"sequence\":3", frames[1]);
        Assert.Contains("test/y", frames[1]);
        Assert.Equal(3, WatchPager.NextCursor(page));
    }
}
