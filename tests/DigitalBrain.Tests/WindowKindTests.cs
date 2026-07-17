using Brain.Client;
using Brain.Contracts;
using DigitalBrain.Tests;
using Brain.Modules.Workspace;
using Xunit;

namespace Brain.KernelTests;

public class WindowKindTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Render_via_typed_proxy_updates_revision_and_state()
    {
        var window = NeuronProxy.Create<IWindow>(Cluster.Client, AddressKey("window", "w1"), OwnerSession);
        var reply = await window.RenderAsync(Blocks.Doc(Blocks.Text("hello")));
        Assert.Equal(1, reply.Revision);

        var snapshot = await Neuron("window", "w1").ReadAsync("document");
        Assert.Contains("hello", snapshot.StateJson);
    }

    [Fact]
    public async Task Second_render_supersedes_previous_content()
    {
        var window = NeuronProxy.Create<IWindow>(Cluster.Client, AddressKey("window", "w2"), OwnerSession);
        await window.RenderAsync(Blocks.Doc(Blocks.Text("first")));
        await window.RenderAsync(Blocks.Doc(Blocks.Text("second")));

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
        var doc = Blocks.Doc(Blocks.Text("test"));
        await window.InvokeAsync(new("window.render.v1", doc.Json, "dup-cmd", OwnerSession));
        await window.InvokeAsync(new("window.render.v1", doc.Json, "dup-cmd", OwnerSession));

        var snapshot = await window.ReadAsync("document");
        Assert.Equal(1, snapshot.Revision);
    }
}
