using Brain.Contracts;
using DigitalBrain.Tests;
using Orleans.TestingHost;
using Xunit;

namespace Brain.ConformanceTests;

public abstract class KindConformance<TConfigurator>(BrainClusterFixture<TConfigurator> fixture)
    : BrainTest<TConfigurator>(fixture)
    where TConfigurator : ISiloConfigurator, new()
{
    protected abstract string KindName { get; }
    protected abstract string SampleContract { get; }
    protected abstract string SampleInputJson { get; }
    protected virtual string NeuronId => $"{KindName}/{Guid.NewGuid():N}";
    protected virtual bool SkipMalformedCheck => false;

    [Fact]
    public async Task Unknown_contract_fails_closed()
    {
        var neuron = Neuron(KindName, NeuronId);
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new("bogus.contract.v1", "{}", "cmd-unknown", OwnerSession)));
        Assert.Equal(BrainErrors.UnknownContract, exception.Code);
        Assert.Empty((await neuron.ReadEventsAsync(0, 1000)).Events);
    }

    [Fact]
    public async Task Duplicate_command_id_replays_byte_identical_receipt()
    {
        var neuron = Neuron(KindName, NeuronId);
        var first = await neuron.InvokeAsync(new(SampleContract, SampleInputJson, "cmd-dup", OwnerSession));
        var countAfterFirst = (await neuron.ReadEventsAsync(0, 1000)).Events.Length;
        var second = await neuron.InvokeAsync(new(SampleContract, SampleInputJson, "cmd-dup", OwnerSession));
        Assert.Equal(first, second);
        Assert.Equal(countAfterFirst, (await neuron.ReadEventsAsync(0, 1000)).Events.Length);
    }

    [Fact]
    public async Task Describe_reports_kind_and_contracts()
    {
        var neuron = Neuron(KindName, NeuronId);
        var description = await neuron.DescribeAsync();
        Assert.Equal(KindName, description.Kind);
        Assert.NotEmpty(description.Contracts);
    }

    [Fact]
    public async Task Malformed_input_fails_closed()
    {
        if (SkipMalformedCheck)
            return;

        var neuron = Neuron(KindName, NeuronId);
        await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new(SampleContract, "{oops", "cmd-malformed", OwnerSession)));
        Assert.Empty((await neuron.ReadEventsAsync(0, 1000)).Events);
    }
}
