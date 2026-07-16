using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class NeuronGrainTests(BrainClusterFixture<KernelKindsConfigurator> fixture)
    : BrainTest<KernelKindsConfigurator>(fixture)
{
    private static NeuronInvocation Echo(string commandId, string input = """{"v":1}""") =>
        new("test.echo.v1", input, commandId, "owner|actor/test|session/t");

    [Fact]
    public async Task Invoke_appends_exactly_one_revision()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(Echo("cmd-1"));
        Assert.Equal(1, receipt.Revision);
        var events = await neuron.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("echoed", events.Events[0].Kind);
    }

    [Fact]
    public async Task Duplicate_command_replays_original_receipt()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        var first = await neuron.InvokeAsync(Echo("cmd-dup"));
        var second = await neuron.InvokeAsync(Echo("cmd-dup", """{"v":2}"""));
        Assert.Equal(first, second);
        Assert.Single((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Wrong_expected_revision_fails_closed()
    {
        var neuron = Neuron("test", Guid.NewGuid().ToString("N"));
        await neuron.InvokeAsync(Echo("cmd-a"));
        var stale = Echo("cmd-b") with { ExpectedRevision = 0 };
        var exception = await Assert.ThrowsAsync<BrainException>(() => neuron.InvokeAsync(stale));
        Assert.Equal(BrainErrors.RevisionConflict, exception.Code);
    }

    [Fact]
    public async Task Unknown_kind_fails_closed_without_state()
    {
        var neuron = Neuron("nope", "x");
        var exception = await Assert.ThrowsAsync<BrainException>(
            () => neuron.InvokeAsync(Echo("cmd-1")));
        Assert.Equal(BrainErrors.UnknownKind, exception.Code);
    }
}
