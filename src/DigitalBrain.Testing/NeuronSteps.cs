using DigitalBrain.Abstractions;
using Reqnroll;

namespace DigitalBrain.Testing;

[Binding]
public sealed class NeuronSteps(Simulation simulation)
{
    [BeforeTestRun]
    public static Task StartCluster() => SimulationCluster.StartAsync();

    [AfterTestRun]
    public static Task StopCluster() => SimulationCluster.StopAsync();

    [Given("a brain for owner {string}")]
    public void GivenABrainForOwner(string owner) => simulation.OpenBrain(owner);

    [BeforeScenario]
    public static void ForgetPreviousScripts() => SimulationCluster.ForgetScripts();

    [When("the client fires {word} at the {word} neuron named {string}")]
    public Task WhenTheClientFires(string synapseType, string neuronType, string name)
        => simulation.ClientFireAsync(synapseType, neuronType, name);

    [When("the client is refused firing {word} at the {word} neuron named {string} owned by {string}")]
    public Task WhenTheClientIsRefused(string synapseType, string neuronType, string name, string targetOwner)
        => simulation.ClientFireExpectingRefusalAsync(synapseType, neuronType, name, targetOwner);

    [Then("the client reads the {word} journal of the {word} neuron named {string}")]
    public async Task ThenTheClientReadsTheJournal(string journal, string neuronType, string name)
    {
        var kind = Enum.Parse<JournalKind>(journal, ignoreCase: true);

        _ = await simulation.ClientReadJournalAsync(kind, neuronType, name, afterSequence: 0);
    }

    [Then("the client reads the {word} journal of its own session")]
    public async Task ThenTheClientReadsItsOwnSessionJournal(string journal)
        => _ = await simulation.ClientReadSessionJournalAsync(Enum.Parse<JournalKind>(journal, ignoreCase: true));

    [When("the session is refused reading the {word} journal of the {word} neuron named {string} owned by {string}")]
    public Task WhenTheSessionIsRefusedReadingAcrossOwners(string journal, string neuronType, string name, string targetOwner)
        => simulation.SessionReadOfForeignOwnerExpectingRefusalAsync(
            Enum.Parse<JournalKind>(journal, ignoreCase: true),
            neuronType,
            name,
            targetOwner);

    [When("a raw cluster client is refused counting {word} subscribers in owner {string}'s registry")]
    public Task WhenARawClientIsRefusedCountingSubscribers(string synapseType, string registryOwner)
        => simulation.RawClientSubscriberCountExpectingRefusalAsync(synapseType, registryOwner);

    [When("a raw cluster client is refused reading the {word} journal of the {word} neuron named {string} owned by {string}")]
    public Task WhenARawClientIsRefusedReading(string journal, string neuronType, string name, string targetOwner)
        => simulation.RawClientReadJournalExpectingRefusalAsync(
            Enum.Parse<JournalKind>(journal, ignoreCase: true),
            neuronType,
            name,
            targetOwner);

    [When("{word} is refused by the {word} neuron named {string}")]
    public Task WhenSynapseIsRefused(string synapseType, string neuronType, string name)
        => simulation.SendExpectingRefusalAsync(synapseType, neuronType, name);

    [Given("the {word} model answers {string} with {string}")]
    public static void GivenTheModelAnswers(string tier, string prompt, string answer)
        => SimulationCluster.Model(Enum.Parse<ModelTier>(tier, ignoreCase: true)).Answer(prompt, answer);

    [Then("the synapse is refused as unscripted")]
    public void ThenTheSynapseIsRefusedAsUnscripted() => simulation.ExpectRefusal<UnscriptedPromptException>();

    [Then("the {word} neuron named {string} answered {string}")]
    public async Task ThenTheNeuronAnswered(string neuronType, string name, string expected)
    {
        var emitted = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            neuronType,
            name,
            afterSequence: 0);

        if (emitted.ResetSnapshot is not null)
        {
            throw new SimulationAssertionException(
                $"The outgoing journal of {neuronType} '{name}' compacted before its answer payloads were asserted.");
        }

        var answers = emitted.Delta
            .Select(delivery => delivery.Synapse)
            .OfType<IAnswer>()
            .Select(answer => answer.Text)
            .ToList();

        if (!answers.Contains(expected, StringComparer.Ordinal))
        {
            throw new SimulationAssertionException(
                $"Expected {neuronType} '{name}' to have answered \"{expected}\", but it emitted {(answers.Count == 0 ? "no answer" : string.Join(", ", answers.Select(answer => $"\"{answer}\"")))}.");
        }
    }

    [When("{word} is sent to the {word} neuron named {string}")]
    public Task WhenSynapseIsSentToNeuron(string synapseType, string neuronType, string name)
        => simulation.SendAsync(synapseType, neuronType, name, new Dictionary<string, string>(StringComparer.Ordinal));

    [When("{word} is sent to the {word} neuron named {string} with")]
    public Task WhenSynapseIsSentToNeuronWith(string synapseType, string neuronType, string name, DataTable values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return simulation.SendAsync(
            synapseType,
            neuronType,
            name,
            values.Rows.ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal));
    }

    [When("{word} is sent twice to the {word} neuron named {string}")]
    public Task WhenTheSameSynapseIsSentTwice(string synapseType, string neuronType, string name)
        => simulation.SendTwiceAsync(synapseType, neuronType, name);

    [Then("the {word} journal of the {word} neuron named {string} contains {word} exactly once")]
    public async Task ThenTheJournalContainsExactlyOnce(string journal, string neuronType, string name, string synapseType)
    {
        var kind = Enum.Parse<JournalKind>(journal, ignoreCase: true);
        var recorded = await simulation.ReadJournalAsync(kind, neuronType, name, afterSequence: 0);
        var expected = NeuronCatalog.SynapseType(synapseType);
        var occurrences = recorded.ResetSnapshot is { } reset
            ? reset.RecordedOf(expected.FullName!)
            : recorded.Delta.Count(delivery => expected.IsInstanceOfType(delivery.Synapse));

        if (occurrences != 1)
        {
            throw new SimulationAssertionException(
                $"Expected exactly one {synapseType} in the {kind} journal of {neuronType} '{name}', but found {occurrences}.");
        }
    }

    [When("{word} is sent to the {word} neuron named {string} claiming owner {string}")]
    public Task WhenSynapseIsSentClaimingAnotherOwner(string synapseType, string neuronType, string name, string claimedOwner)
        => simulation.SendClaimingOwnerAsync(synapseType, neuronType, name, claimedOwner);

    [Then("the synapse is refused as unauthorized")]
    public void ThenTheSynapseIsRefusedAsUnauthorized() => simulation.ExpectRefusal<NeuronAuthorizationException>();

    [Then("the incoming journal of the {word} neuron named {string} is empty")]
    public async Task ThenTheIncomingJournalIsEmpty(string neuronType, string name)
    {
        var journal = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            neuronType,
            name,
            afterSequence: 0);
        var recorded = journal.ResetSnapshot?.TotalRecorded ?? journal.Delta.Count;

        if (recorded > 0)
        {
            throw new SimulationAssertionException(
                $"Expected the incoming journal of {neuronType} '{name}' to be empty, but it recorded {recorded} synapse(s).");
        }
    }

    [When("the {word} neuron named {string} subscribes to {word} in owner {string}'s registry")]
    public Task WhenANeuronSubscribesInAnotherOwnersRegistry(string neuronType, string name, string synapseType, string registryOwner)
        => simulation.SubscribeInOwnerExpectingRefusalAsync(neuronType, name, synapseType, registryOwner);

    [Then("the incoming journal of owner {string}'s {word} neuron named {string} is empty")]
    public static async Task ThenTheIncomingJournalOfAnotherOwnerIsEmpty(string owner, string neuronType, string name)
    {
        var journal = await Simulation.ReadJournalOfOwnerAsync(
            JournalKind.Incoming,
            owner,
            neuronType,
            name,
            afterSequence: 0);
        var recorded = journal.ResetSnapshot?.TotalRecorded ?? journal.Delta.Count;

        if (recorded > 0)
        {
            throw new SimulationAssertionException(
                $"Expected the incoming journal of owner '{owner}' {neuronType} '{name}' to be empty, but it recorded {recorded} synapse(s).");
        }
    }

    [Given("a {word} neuron named {string} is registered")]
    [When("a {word} neuron named {string} is registered")]
    public Task ANeuronIsRegistered(string neuronType, string name) => simulation.RegisterAsync(neuronType, name);

    [Then("the incoming journal of the {word} neuron named {string} settles below {int} synapses")]
    public async Task ThenTheIncomingJournalSettlesBelow(string neuronType, string name, int limit)
    {
        var settled = await simulation.SettleAsync(JournalKind.Incoming, neuronType, name);

        if (settled >= limit)
        {
            throw new SimulationAssertionException(
                $"Expected the incoming journal of {neuronType} '{name}' to settle below {limit} synapses, but it reached {settled}.");
        }
    }

    [When("the silo hosting the {word} neuron named {string} is restarted")]
    public Task WhenTheHostingSiloIsRestarted(string neuronType, string name)
        => SimulationCluster.RestartHostOfAsync(simulation.NeuronNamed(neuronType, name));

    [Given("{int} {word} neurons are registered")]
    public Task ManyNeuronsAreRegistered(int count, string neuronType) => simulation.RegisterManyAsync(count, neuronType);

    [Then("the {word} neuron named {string} and the {word} neuron named {string} are hosted on different silos")]
    public async Task ThenTheTwoNeuronsAreOnDifferentSilos(string firstType, string firstName, string secondType, string secondName)
    {
        var first = simulation.NeuronNamed(firstType, firstName);
        var second = simulation.NeuronNamed(secondType, secondName);
        var silos = await Simulation.HostingSiloCountAsync(first, second);

        if (silos != 2)
        {
            throw new SimulationAssertionException(
                $"Expected {first} and {second} to be pinned to different silos, but they resolve to {silos} distinct silo(s).");
        }
    }

    [Then("every registered {word} received {word}")]
    public Task ThenEveryRegisteredNeuronReceived(string neuronType, string synapseType)
        => simulation.AwaitAllRegisteredHandledAsync(synapseType);

    [Then("the subscriber count for {word} has grown by {int}")]
    public async Task ThenTheSubscriberCountHasGrownBy(string synapseType, int expected)
    {
        var actual = await simulation.SubscriberCountAsync(synapseType);

        if (actual != expected)
        {
            throw new SimulationAssertionException(
                $"Expected the subscriber count for {synapseType} to have grown by {expected}, but it is {actual}.");
        }
    }

    [Then("the incoming journal of the {word} neuron named {string} contains {word}")]
    public async Task ThenTheIncomingJournalContains(string neuronType, string name, string synapseType)
    {
        await simulation.AwaitHandledAsync(neuronType, name, synapseType);
        await AssertJournalContains(JournalKind.Incoming, neuronType, name, synapseType);
    }

    [Then("the outgoing journal of the {word} neuron named {string} contains {word}")]
    public Task ThenTheOutgoingJournalContains(string neuronType, string name, string synapseType)
        => AssertJournalContains(JournalKind.Outgoing, neuronType, name, synapseType);

    private async Task AssertJournalContains(JournalKind kind, string neuronType, string name, string synapseType)
    {
        var journal = await simulation.ReadJournalAsync(kind, neuronType, name, afterSequence: 0);
        var expected = NeuronCatalog.SynapseType(synapseType);

        if (journal.ResetSnapshot is { } reset)
        {
            if (reset.RecordedOf(expected.FullName!) > 0)
            {
                return;
            }

            throw new SimulationAssertionException(
                $"Expected the {kind} journal of {neuronType} '{name}' to contain {synapseType}, but its snapshot recorded no such synapse.");
        }

        if (!journal.Delta.Any(delivery => expected.IsInstanceOfType(delivery.Synapse)))
        {
            var recorded = journal.Delta.Count == 0
                ? "nothing"
                : string.Join(", ", journal.Delta.Select(delivery => delivery.Synapse.GetType().Name));

            throw new SimulationAssertionException(
                $"Expected the {kind} journal of {neuronType} '{name}' to contain {synapseType}, but it recorded {recorded}.");
        }
    }
}
