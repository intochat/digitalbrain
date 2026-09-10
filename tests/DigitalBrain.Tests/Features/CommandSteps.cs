using System.Diagnostics;
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

    [When(@"""(.*)"" adds (\d+) to counter ""(.*)"" with command id ""(.*)""")]
    public Task Add(string principal, int amount, string name, string handle)
        => Capture(principal, () => Counter(name).Add(new AddCount(CommandIdFrom(handle), amount)));

    [When(@"""(.*)"" adds (\d+) to counter ""(.*)"" with command id ""(.*)"" and the call fails")]
    public async Task AddAndFail(string principal, int amount, string name, string handle)
    {
        await Add(principal, amount, name, handle);
        Assert.NotNull(_lastError);
    }

    [When(@"""(.*)"" adds a (\d+)-byte note to counter ""(.*)"" with command id ""(.*)""")]
    public Task AddNote(string principal, int bytes, string name, string handle)
        => Capture(principal, () => Counter(name).Add(new AddCount(CommandIdFrom(handle), 0, new string('n', bytes))));

    [When(@"""(.*)"" invokes the misbehaving save on counter ""(.*)""")]
    public Task AddAndSave(string principal, string name)
        => Capture(principal, () => Counter(name).AddAndSave(new AddCount(CommandIdFrom("save"), 3)));

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

    [Then(@"""(.*)"" waits up to 5 seconds until counter ""(.*)"" total is (\d+)")]
    public async Task WaitForTotal(string principal, string name, int expected)
    {
        var elapsed = Stopwatch.StartNew();
        int total;
        do
        {
            RequestContext.Set(CallerContext.Key, NeuronId.Plain(principal).ToString());
            total = await Counter(name).ReadTotal();
            if (total == expected || elapsed.Elapsed >= TimeSpan.FromSeconds(5))
            {
                break;
            }

            await Task.Delay(50);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.Equal(expected, total);
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
        Assert.Contains(fragment, Flatten(_lastError).Message, StringComparison.Ordinal);
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

    private async Task Capture(string principal, Func<Task<Accepted<int>>> call)
    {
        _lastError = null;
        RequestContext.Set(CallerContext.Key, NeuronId.Plain(principal).ToString());
        try
        {
            _results.Add(await call());
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

    private static CommandId CommandIdFrom(string handle)
        => new CommandId(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(handle)).AsSpan(0, 16)));

    private static void AssertPhases(IEnumerable<CommandRecord> records, string first, string second)
        => Assert.Equal(
            [Enum.Parse<CommandPhase>(first), Enum.Parse<CommandPhase>(second)],
            records.Select(record => record.Phase));

    private static Exception Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : error;
}
