using Brain.Client;
using Brain.Contracts;
using DigitalBrain.Tests;
using Flutter.Contracts;
using Xunit;

namespace Brain.KernelTests;

public class WindowKindTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Render_via_typed_proxy_updates_revision_and_state()
    {
        var window = NeuronProxy.Create<IWindowNeuron>(Cluster.Client, AddressKey("window", "w1"), OwnerSession);
        var reply = await window.RenderAsync(Document("hello"));
        Assert.Equal(1, reply.Revision);

        var snapshot = await Neuron("window", "w1").ReadAsync("document");
        Assert.Contains("hello", snapshot.StateJson);
    }

    [Fact]
    public async Task Second_render_supersedes_previous_content()
    {
        var window = NeuronProxy.Create<IWindowNeuron>(Cluster.Client, AddressKey("window", "w2"), OwnerSession);
        await window.RenderAsync(Document("first"));
        await window.RenderAsync(Document("second"));

        var snapshot = await Neuron("window", "w2").ReadAsync("document");
        Assert.Contains("second", snapshot.StateJson);
        Assert.DoesNotContain("first", snapshot.StateJson);
    }

    [Fact]
    public async Task Invalid_doc_json_fails_closed()
    {
        var window = Neuron("window", "w3");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            window.InvokeAsync(new("window.render.v1", "{oops", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await window.ReadAsync("document")).Revision);
    }

    [Fact]
    public async Task Duplicate_commandId_records_single_event()
    {
        var window = Neuron("window", "w4");
        var json = System.Text.Json.JsonSerializer.Serialize(Document("test"), System.Text.Json.JsonSerializerOptions.Web);
        await window.InvokeAsync(new("window.render.v1", json, "dup-cmd", OwnerSession));
        await window.InvokeAsync(new("window.render.v1", json, "dup-cmd", OwnerSession));

        var snapshot = await window.ReadAsync("document");
        Assert.Equal(1, snapshot.Revision);
    }

    private static UiDocument Document(string text) =>
        new(1, [new UiBlock("text", Text: text)]);
}
