using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Runtime.Neurons;
using Orleans;
using Orleans.Journaling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace OrleansExamples.Tests;

[GenerateSerializer]
public record TestSynapse([property: Id(0)] string Content) : Synapse;

[GenerateSerializer]
public record WriteFileSynapse([property: Id(0)] string FilePath, [property: Id(1)] string Content) : Synapse;

[GenerateSerializer]
public record ReadFileSynapse([property: Id(0)] string FilePath) : Synapse;

[GenerateSerializer]
public record FileResponseSynapse([property: Id(0)] string FilePath, [property: Id(1)] string Content, [property: Id(2)] bool Success) : Synapse;

public class TestNeuron : Neuron, IHandle<TestSynapse>, IHandle<FileResponseSynapse>
{
    public bool TestSynapseReceived { get; private set; }
    public string? LastTestSynapseContent { get; private set; }
    
    public bool FileResponseReceived { get; private set; }
    public FileResponseSynapse? LastFileResponse { get; private set; }

    public TestNeuron(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger<TestNeuron> logger)
        : base(incoming, outgoing, grains, logger)
    {
    }

    public Task HandleAsync(TestSynapse synapse, CancellationToken cancellationToken)
    {
        TestSynapseReceived = true;
        LastTestSynapseContent = synapse.Content;
        return Task.CompletedTask;
    }

    public Task HandleAsync(FileResponseSynapse synapse, CancellationToken cancellationToken)
    {
        FileResponseReceived = true;
        LastFileResponse = synapse;
        return Task.CompletedTask;
    }

    public async Task TriggerFireAsync(Synapse synapse)
    {
        await FireSynapseAsync(synapse);
    }
}

public class MockWindowsFileSystemNeuron : Neuron, IHandle<WriteFileSynapse>, IHandle<ReadFileSynapse>
{
    private static readonly ConcurrentDictionary<string, string> MockFilesystem = new(StringComparer.OrdinalIgnoreCase);

    public MockWindowsFileSystemNeuron(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger<MockWindowsFileSystemNeuron> logger)
        : base(incoming, outgoing, grains, logger)
    {
    }

    public static void ClearFilesystem() => MockFilesystem.Clear();

    public async Task HandleAsync(WriteFileSynapse synapse, CancellationToken cancellationToken)
    {
        MockFilesystem[synapse.FilePath] = synapse.Content;
        
        var response = new FileResponseSynapse(synapse.FilePath, synapse.Content, true)
        {
            ReceiverNeuronType = synapse.CallerNeuronType ?? nameof(TestNeuron),
            ReceiverNeuronId = synapse.CallerNeuronId
        };
        await FireSynapseAsync(response);
    }

    public async Task HandleAsync(ReadFileSynapse synapse, CancellationToken cancellationToken)
    {
        bool found = MockFilesystem.TryGetValue(synapse.FilePath, out var content);
        
        var response = new FileResponseSynapse(synapse.FilePath, content ?? "", found)
        {
            ReceiverNeuronType = synapse.CallerNeuronType ?? nameof(TestNeuron),
            ReceiverNeuronId = synapse.CallerNeuronId
        };
        await FireSynapseAsync(response);
    }
}
