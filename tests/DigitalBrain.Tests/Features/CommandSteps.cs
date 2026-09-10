using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Orleans.Runtime;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class CommandSteps(BrainSteps brain)
{
    private readonly List<Accepted<int>> _results = [];
    private Exception? _lastError;
    private CommandId _lastCommandId;

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
    public static void ThenExecuted(string name, int times)
        => Assert.Equal(times, FixtureSwitches.CommandExecutions.GetValueOrDefault(name));

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
        var error = Flatten(_lastError);
        switch (error)
        {
            case CommandRejectedException rejected:
                Assert.Equal(_lastCommandId, rejected.Id);
                break;
            case CommandOutcomeUnknownException unknown:
                Assert.Equal(_lastCommandId, unknown.Id);
                break;
            case CommandFailedException failed:
                Assert.Equal(_lastCommandId, failed.Id);
                break;
        }

        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    [Then("the add fails with the membrane message")]
    public void ThenMembraneFailure() => ThenFails("the limit is 64 KB");

    // Feature classes run in parallel, so this reset must not reach other features.
    [AfterScenario]
    [Scope(Feature = "Command")]
    public static void AfterScenario()
    {
        FixtureSwitches.CommandExecutions.Clear();
        FixtureCommandCrashPoint.CrashOnce.Clear();
    }

    private async Task Capture(string principal, string handle, CommandId id, Func<CommandId, Task<Accepted<int>>> call)
    {
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

    private static Exception Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : error;
}
