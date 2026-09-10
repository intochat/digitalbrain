using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class CommandSteps(BrainSteps brain)
{
    private readonly List<Accepted<int>> _results = [];
    private Exception? _lastError;
    private Exception? _previousError;
    private CommandId _lastCommandId;
    private readonly Dictionary<string, (long Incoming, long Outgoing, long Commands)> _journalSizes = new(StringComparer.Ordinal);

    [Given(@"counter ""(.*)"" fails every reaction")]
    public void FailEveryReaction(string name)
        => FixtureState.FailingReactions[name] = 0;

    [Given(@"counter ""(.*)"" loses its turn after recording Attempted for command id ""(.*)""")]
    public void LoseTurnAfterAttempted(string name, string handle)
        => FixtureState.LostTurns[CommandIdFrom(handle)] = name;

    [When(@"counter ""(.*)"" journal sizes are recorded")]
    public async Task RecordJournalSizes(string name) => _journalSizes[name] = await JournalSizes(name);

    [When(@"counter ""(.*)"" total is read (\d+) times")]
    public async Task ReadTotalRepeatedly(string name, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Counter(name).ReadTotal();
        }
    }

    [Then(@"counter ""(.*)"" journal sizes are unchanged")]
    public async Task JournalSizesUnchanged(string name) => Assert.Equal(_journalSizes[name], await JournalSizes(name));

    [Then(@"""(.*)"" commands journal is empty")]
    public async Task CommandJournalEmpty(string name)
    {
        var read = await Journal(name);
        Assert.Empty(read.Delta);
        Assert.Equal(0, read.ResumeSequence);
    }

    private CounterFixtureState FixtureState => brain.Brain.SiloServices.GetRequiredService<CounterFixtureState>();

    private async Task<(long Incoming, long Outgoing, long Commands)> JournalSizes(string name)
    {
        var counter = Counter(name);
        var incoming = counter.ReadJournal(JournalKind.Incoming, 0);
        var outgoing = counter.ReadJournal(JournalKind.Outgoing, 0);
        var commands = counter.ReadCommands(0);
        await Task.WhenAll(incoming, outgoing, commands);
        return ((await incoming).ResumeSequence, (await outgoing).ResumeSequence, (await commands).ResumeSequence);
    }

    internal Dictionary<string, SignalId> WorkByCommand { get; } = new(StringComparer.Ordinal);

    [When(@"""(.*)"" adds (\d+) to counter ""(.*)"" with command id ""(.*)""")]
    public Task Add(string principal, int amount, string name, string handle)
        => Capture(principal, handle, CommandIdFrom(handle), id => Counter(name).Add(new AddCount(id, amount)));

    [When(@"""(.*)"" adds (\d+) to counter ""(.*)"" with command id ""(.*)"" and the call fails")]
    public async Task AddAndFail(string principal, int amount, string name, string handle)
    {
        await Add(principal, amount, name, handle);
        Assert.NotNull(_lastError);
    }

    [When(@"""(.*)"" adds a (\d+)-byte note to counter ""(.*)"" with command id ""(.*)""")]
    public Task AddNote(string principal, int bytes, string name, string handle)
        => Capture(principal, handle, CommandIdFrom(handle), id => Counter(name).Add(new AddCount(id, 0, new string('n', bytes))));

    [When(@"""(.*)"" invokes the misbehaving save on counter ""(.*)""")]
    public Task AddAndSave(string principal, string name)
        => Capture(principal, "save", CommandIdFrom("save"), id => Counter(name).AddAndSave(new AddCount(id, 3)));

    [When(@"""(.*)"" invokes the misbehaving call-out on counter ""(.*)""")]
    public Task AddAndCallOut(string principal, string name)
        => Capture(principal, "callout", CommandIdFrom("callout"), id => Counter(name).AddAndCallOut(new AddCount(id, 3)));

    // The scenario that uses this step issues only command "x9".
    [Given(@"^counter ""[^""]*"" crashes after recording Attempted$")]
    public static void CrashAfterAttempted() => FixtureCommandCrashPoint.CrashOnce[CommandIdFrom("x9")] = 0;

    [Then("the add is accepted with a work id")]
    public void ThenAccepted()
    {
        Assert.Null(_lastError);
        Assert.NotEmpty(_results);
        Assert.NotEqual(default, _results[^1].Work);
    }

    [Then(@"""(.*)"" commands journal shows ""(.*)"" as (\w+) then (\w+)")]
    public async Task ThenCommandPhases(string name, string handle, string first, string second)
    {
        var read = await Journal(name);
        AssertPhases(read.Delta.Where(record => record.Id == CommandIdFrom(handle)), first, second);
    }

    [Then(@"""(.*)"" commands journal shows ""(.*)"" incarnation (\d+) as (\w+) then (\w+)")]
    public async Task ThenIncarnationPhases(string name, string handle, int incarnation, string first, string second)
    {
        var read = await Journal(name);
        AssertPhases(
            read.Delta.Where(record => record.Id == CommandIdFrom(handle) && record.Incarnation == incarnation),
            first, second);
    }

    [Then(@"""(.*)"" commands journal shows one Rejected record")]
    public async Task ThenRejectedRecord(string name)
    {
        var read = await Journal(name);
        var rejected = Assert.Single(read.Delta, record => record.Phase == CommandPhase.Rejected);
        Assert.Null(rejected.ArgsJson);
    }

    [When(@"""(.*)"" waits up to (\d+) seconds until counter ""(.*)"" total is (\d+)")]
    [Then(@"""(.*)"" waits up to (\d+) seconds until counter ""(.*)"" total is (\d+)")]
    public async Task WaitForTotal(string principal, int seconds, string name, int expected)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var counter = Counter(name);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var observed = await counter.ReadTotal();
        while (observed != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            observed = await counter.ReadTotal();
        }

        if (observed != expected)
        {
            Assert.Fail($"{principal} waited {seconds}s for {name} total to be {expected}, but observed {observed}");
        }
    }

    [Then(@"counter ""(.*)"" executed (\d+) times?")]
    public void ThenExecuted(string name, int times)
        => Assert.Equal(times, FixtureState.Executions.GetValueOrDefault(name));

    [Then("both adds return the same work id")]
    public void ThenSameWork()
    {
        Assert.Null(_lastError);
        Assert.Equal(2, _results.Count);
        Assert.Equal(_results[0].Work, _results[1].Work);
    }

    [Then(@"the second add fails with ""(.*)""")]
    [Then(@"it fails with ""(.*)""")]
    public void ThenFails(string fragment)
    {
        Assert.NotNull(_lastError);
        var error = BrainSteps.Flatten(_lastError);
        var commandId = error switch
        {
            CommandRejectedException rejected => rejected.Id,
            CommandOutcomeUnknownException unknown => unknown.Id,
            CommandFailedException failed => failed.Id,
            _ => _lastCommandId,
        };
        Assert.Equal(_lastCommandId, commandId);

        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    [Then("the add fails with the membrane message")]
    public void ThenMembraneFailure() => ThenFails("the limit is 64 KB");

    [Then("the repeated add fails with the same rejection")]
    public void ThenSameRejection()
    {
        Assert.NotNull(_previousError);
        Assert.NotNull(_lastError);
        var previous = Assert.IsType<CommandRejectedException>(BrainSteps.Flatten(_previousError));
        var current = Assert.IsType<CommandRejectedException>(BrainSteps.Flatten(_lastError));
        Assert.Equal(previous.Id, current.Id);
        Assert.Equal(previous.Reason, current.Reason);
        Assert.Equal(previous.Message, current.Message);
    }

    // Feature classes run in parallel, so this reset must not reach other features.
    [AfterScenario]
    [Scope(Feature = "Command")]
    public static void AfterScenario()
    {
        FixtureCommandCrashPoint.CrashOnce.Clear();
    }

    private async Task Capture(string principal, string handle, CommandId id, Func<CommandId, Task<Accepted<int>>> call)
    {
        _previousError = _lastError;
        _lastError = null;
        _lastCommandId = id;
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        try
        {
            var accepted = await call(id);
            _results.Add(accepted);
            WorkByCommand[handle] = accepted.Work;
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }

    private ICounter Counter(string name)
        => brain.Brain.Grains.GetGrain<ICounter>(new NeuronId("counter", name).ToGrainId());

    private Task<CommandJournalRead> Journal(string name)
        => brain.Brain.Grains.GetGrain<INeuron>(new NeuronId("counter", name).ToGrainId()).ReadCommands(0);

    internal static CommandId CommandIdFrom(string handle)
        => new CommandId(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(handle)).AsSpan(0, 16)));

    private static void AssertPhases(IEnumerable<CommandRecord> records, string first, string second)
        => Assert.Equal(
            [Enum.Parse<CommandPhase>(first), Enum.Parse<CommandPhase>(second)],
            records.Select(record => record.Phase));
}
