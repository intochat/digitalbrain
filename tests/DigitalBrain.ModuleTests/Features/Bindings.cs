using DigitalBrain.Abstractions;
using DigitalBrain.Generated;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.ModuleTests.Features;

[Binding]
internal sealed class Bindings
{
    private TestBrain? _brain;
    private Exception? _failure;
    private readonly Dictionary<(TestJournal Journal, string Synapse), int>
        _observed = [];
    private string? _correlationInstance;
    private string? _ownerLabel;

    [AfterScenario]
    public async Task DisposeBrain()
    {
        if (Interlocked.Exchange(ref _brain, null) is { } brain)
        {
            await brain.DisposeAsync();
        }

        _failure = null;
        _observed.Clear();
        _correlationInstance = null;
    }

    [Given("a brain for owner {string}")]
    public async Task GivenABrainForOwner(string owner)
    {
        if (_brain is not null)
        {
            throw new InvalidOperationException(
                "A Gherkin scenario opens exactly one method-scoped TestBrain.");
        }

        var fixture = await TestContext.Current.GetFixture<ModuleFixture>()
            ?? throw new InvalidOperationException(
                "The assembly-owned ModuleFixture is unavailable.");
        _brain = await fixture.CreateBrainAsync(
            TestContext.Current.CancellationToken);
        _brain.ConfigureModuleParameters();
        _ownerLabel = owner;
    }

    [Given("owner {string} has the {word} neuron named {string}")]
    public void GivenOwnerHasANeuron(
        string owner,
        string neuronName,
        string instance)
        => _ = Neuron(neuronName).Open(
            Brain().Owner(owner),
            instance);

    [When("the client sends {word} to the {word} neuron named {string} with")]
    public Task WhenTheClientSends(
        string synapseName,
        string neuronName,
        string instance,
        DataTable arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var owner = Owner();
        var neuron = Neuron(neuronName);
        var synapse = Synapse(
            synapseName,
            arguments.Rows.ToDictionary(
                static row => row[0],
                static row => row[1],
                StringComparer.OrdinalIgnoreCase));

        return neuron.Send(owner.Client, instance, synapse);
    }

    [When("the client sends {word} to the {word} neuron named {string}")]
    public Task WhenTheClientSends(
        string synapseName,
        string neuronName,
        string instance)
        => Neuron(neuronName).Send(
            Owner().Client,
            instance,
            Synapse(synapseName, EmptyArguments));

    [When("owner {string} sends {word} to owner {string}'s {word} neuron named {string}")]
    public async Task WhenAnOwnerSendsToAnotherOwnersNeuron(
        string sender,
        string synapseName,
        string receiver,
        string neuronName,
        string instance)
    {
        var target = Neuron(neuronName).Open(
            Brain().Owner(receiver),
            instance);
        _failure = null;

        try
        {
            await Brain().Owner(sender).Client.SendAsync(
                target.Id,
                Synapse(synapseName, EmptyArguments));
        }
        catch (NeuronAuthorizationException failure)
        {
            _failure = failure;
        }
    }

    [When("the client broadcasts {word}")]
    public Task WhenTheClientBroadcasts(string synapseName)
        => Owner().Client.EmitAsync(
            Synapse(synapseName, EmptyArguments));

    [When("the {word} neuron named {string} restarts")]
    public Task WhenTheNeuronRestarts(
        string neuronName,
        string instance)
        => Neuron(neuronName)
            .Open(Owner(), instance)
            .Restart(TestContext.Current.CancellationToken);

    [Then("the {word} journal of the {word} neuron named {string} contains {word}")]
    public async Task ThenTheJournalContains(
        string direction,
        string neuronName,
        string instance,
        string synapseName)
    {
        var neuron = Neuron(neuronName).Open(Owner(), instance);
        var journal = direction switch
        {
            "incoming" => neuron.Incoming,
            "outgoing" => neuron.Outgoing,
            _ => throw new ArgumentException(
                $"Unknown journal direction '{direction}'.",
                nameof(direction)),
        };

        var synapse = SynapseContract(synapseName);
        var observation = await synapse.Next(
            journal,
            TestContext.Current.CancellationToken);
        _correlationInstance = observation.CorrelationInstance;
        RecordObservation(journal, synapse.Identity);
    }

    [Then("the {word} journal of the {word} neuron named {string} contains {word} exactly {int} times")]
    public async Task ThenTheJournalContainsExactly(
        string direction,
        string neuronName,
        string instance,
        string synapseName,
        int expected)
    {
        var neuron = Neuron(neuronName).Open(Owner(), instance);
        var journal = direction switch
        {
            "incoming" => neuron.Incoming,
            "outgoing" => neuron.Outgoing,
            _ => throw new ArgumentException(
                $"Unknown journal direction '{direction}'.",
                nameof(direction)),
        };
        var synapse = SynapseContract(synapseName);
        var key = (journal, synapse.Identity);
        _observed.TryGetValue(key, out var observed);

        while (observed < expected)
        {
            await synapse.Next(
                journal,
                TestContext.Current.CancellationToken);
            observed++;
            _observed[key] = observed;
        }

        Assert.Equal(
            expected,
            await synapse.Count(
                journal,
                0,
                TestContext.Current.CancellationToken));
    }

    [Then("the {word} journal of the {word} neuron at that correlation contains {word} exactly {int} times")]
    public Task ThenTheJournalAtThatCorrelationContainsExactly(
        string direction,
        string neuronName,
        string synapseName,
        int expected)
        => ThenTheJournalContainsExactly(
            direction,
            neuronName,
            _correlationInstance
                ?? throw new InvalidOperationException(
                    "Observe a synapse before following its correlation."),
            synapseName,
            expected);

    [Then("the client's outgoing journal contains {word}")]
    public async Task ThenTheClientsOutgoingJournalContains(
        string synapseName)
    {
        var session = Neuron(
                "DigitalBrain.Abstractions.ISessionNeuron")
            .Open(Owner(), "session");
        var synapse = SynapseContract(synapseName);

        await synapse.Next(
            session.Outgoing,
            TestContext.Current.CancellationToken);
        RecordObservation(session.Outgoing, synapse.Identity);
    }

    [Then("the request is rejected as unauthorized")]
    public void ThenTheRequestIsRejectedAsUnauthorized()
    {
        var failure = Assert.IsAssignableFrom<Exception>(_failure);

        Assert.Contains(
            Failures(failure),
            candidate => candidate is NeuronAuthorizationException);
    }

    private TestBrain Brain()
        => _brain
            ?? throw new InvalidOperationException(
                "Open a brain before using its generated vocabulary.");

    private TestOwner Owner()
        => Brain().Owner(
            _ownerLabel
                ?? throw new InvalidOperationException(
                    "Open an owner before using its generated vocabulary."));

    private static TestNeuronContract Neuron(string name)
        => GeneratedTestVocabulary.TryResolveNeuron(name, out var contract)
            ? contract
            : throw new ArgumentException(
                $"Neuron vocabulary '{name}' is unknown or ambiguous.",
                nameof(name));

    private static DigitalBrain.Abstractions.Synapse Synapse(
        string name,
        IReadOnlyDictionary<string, string> arguments)
        => GeneratedTestVocabulary.TryCreateSynapse(
            name,
            arguments,
            out var synapse)
            ? synapse
            : throw new ArgumentException(
                $"Synapse vocabulary '{name}' is unknown or ambiguous.",
                nameof(name));

    private static TestSynapseContract SynapseContract(string name)
        => GeneratedTestVocabulary.TryResolveSynapse(name, out var contract)
            ? contract
            : throw new ArgumentException(
                $"Synapse vocabulary '{name}' is unknown or ambiguous.",
                nameof(name));

    private void RecordObservation(
        TestJournal journal,
        string synapse)
    {
        var key = (journal, synapse);
        _observed.TryGetValue(key, out var count);
        _observed[key] = count + 1;
    }

    private static IEnumerable<Exception> Failures(Exception failure)
    {
        for (var current = failure;
            current is not null;
            current = current.InnerException)
        {
            yield return current;
        }
    }

    private static IReadOnlyDictionary<string, string> EmptyArguments { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
