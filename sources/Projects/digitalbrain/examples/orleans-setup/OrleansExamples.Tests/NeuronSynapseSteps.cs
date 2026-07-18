using System;
using System.Threading.Tasks;
using DigitalBrain.SDK;
using DigitalBrain.Runtime.Neurons;
using FluentAssertions;
using Reqnroll;

namespace OrleansExamples.Tests;

[Binding]
public class NeuronSynapseSteps
{
    private NeuronTestHarness<TestNeuron>? _testNeuronHarness;
    private NeuronTestHarness<MockWindowsFileSystemNeuron>? _fsNeuronHarness;
    
    private NeuronTestExecutionResult? _testResult;
    private FileResponseSynapse? _lastFileResponse;

    [Given(@"a TestNeuron is initialized")]
    public void GivenATestNeuronIsInitialized()
    {
        var builder = new NeuronBuilder<TestNeuron>();
        _testNeuronHarness = builder.Build();
    }

    [When(@"it receives a TestSynapse containing ""(.*)""")]
    public async Task WhenItReceivesATestSynapseContaining(string content)
    {
        var synapse = new TestSynapse(content);
        _testResult = await _testNeuronHarness!.TestReceiveAsync(synapse);
    }

    [Then(@"the TestNeuron should mark the synapse as received")]
    public void ThenTheTestNeuronShouldMarkTheSynapseAsReceived()
    {
        _testNeuronHarness!.Instance.TestSynapseReceived.Should().BeTrue();
    }

    [Then(@"the last received content should be ""(.*)""")]
    public void ThenTheLastReceivedContentShouldBe(string content)
    {
        _testNeuronHarness!.Instance.LastTestSynapseContent.Should().Be(content);
    }

    [Then(@"the synapse should be recorded in the incoming journal")]
    public async Task ThenTheSynapseShouldBeRecordedInTheIncomingJournal()
    {
        var count = await _testNeuronHarness!.Instance.GetIncomingCountAsync();
        count.Should().BeGreaterThan(0);
        
        var journal = await _testNeuronHarness.Instance.GetIncomingJournalAsync();
        journal.Should().ContainSingle(s => s is TestSynapse);
    }

    [When(@"it fires a TestSynapse containing ""(.*)""")]
    public async Task WhenItFiresATestSynapseContaining(string content)
    {
        var synapse = new TestSynapse(content);
        await _testNeuronHarness!.Instance.TriggerFireAsync(synapse);
    }

    [Then(@"the synapse should be recorded in the outgoing journal")]
    public async Task ThenTheSynapseShouldBeRecordedInTheOutgoingJournal()
    {
        var count = await _testNeuronHarness!.Instance.GetOutgoingCountAsync();
        count.Should().BeGreaterThan(0);
        
        var journal = await _testNeuronHarness.Instance.GetOutgoingJournalAsync();
        journal.Should().ContainSingle(s => s is TestSynapse);
    }

    [When(@"it fires a TestSynapse containing ""(.*)"" with broadcast routing mode")]
    public async Task WhenItFiresATestSynapseContainingWithBroadcastRoutingMode(string content)
    {
        var synapse = new TestSynapse(content)
        {
            RoutingMode = RoutingMode.Broadcast
        };
        await _testNeuronHarness!.Instance.TriggerFireAsync(synapse);
    }

    [Then(@"the fired synapse should have broadcast routing mode")]
    public void ThenTheFiredSynapseShouldHaveBroadcastRoutingMode()
    {
        var firedSynapses = GetFiredSynapses(_testNeuronHarness!);
        firedSynapses.Should().NotBeEmpty();
        var lastFired = firedSynapses[^1];
        lastFired.Payload.RoutingMode.Should().Be(RoutingMode.Broadcast);
    }

    [Given(@"the filesystem is cleared")]
    public void GivenTheFilesystemIsCleared()
    {
        MockWindowsFileSystemNeuron.ClearFilesystem();
    }

    [Given(@"a MockWindowsFileSystemNeuron is initialized")]
    public void GivenAMockWindowsFileSystemNeuronIsInitialized()
    {
        var builder = new NeuronBuilder<MockWindowsFileSystemNeuron>();
        _fsNeuronHarness = builder.Build();
    }

    [When(@"a WriteFileSynapse is sent to write ""(.*)"" to ""(.*)""")]
    public async Task WhenAWriteFileSynapseIsSentToWriteTo(string content, string filePath)
    {
        var synapse = new WriteFileSynapse(filePath, content)
        {
            CallerNeuronType = nameof(TestNeuron),
            CallerNeuronId = Guid.NewGuid()
        };
        _testResult = await _fsNeuronHarness!.TestReceiveAsync(synapse);
    }

    [Then(@"a file response should be received confirming the write operation")]
    public void ThenAFileResponseShouldBeReceivedConfirmingTheWriteOperation()
    {
        var firedSynapses = GetFiredSynapses(_fsNeuronHarness!);
        firedSynapses.Should().NotBeEmpty();
        var fired = firedSynapses[^1].Payload as FileResponseSynapse;
        fired.Should().NotBeNull();
        fired!.Success.Should().BeTrue();
        _lastFileResponse = fired;
    }

    [When(@"a ReadFileSynapse is sent to read from ""(.*)""")]
    public async Task WhenAReadFileSynapseIsSentToReadFrom(string filePath)
    {
        var synapse = new ReadFileSynapse(filePath)
        {
            CallerNeuronType = nameof(TestNeuron),
            CallerNeuronId = Guid.NewGuid()
        };
        _testResult = await _fsNeuronHarness!.TestReceiveAsync(synapse);
    }

    [Then(@"a file response should be received containing ""(.*)"" and confirming success")]
    public void ThenAFileResponseShouldBeReceivedContainingAndConfirmingSuccess(string content)
    {
        var firedSynapses = GetFiredSynapses(_fsNeuronHarness!);
        firedSynapses.Should().NotBeEmpty();
        var fired = firedSynapses[^1].Payload as FileResponseSynapse;
        fired.Should().NotBeNull();
        fired!.Success.Should().BeTrue();
        fired.Content.Should().Be(content);
    }

    private System.Collections.Generic.IReadOnlyList<FiredSynapse> GetFiredSynapses<T>(NeuronTestHarness<T> harness) where T : Neuron
    {
        var field = typeof(NeuronTestHarness<T>).GetField("_firedSynapses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (System.Collections.Generic.IReadOnlyList<FiredSynapse>)field!.GetValue(harness)!;
    }
}
