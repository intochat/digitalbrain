using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Memory;
using Orleans.Runtime;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class MemorySteps(BrainWorld world)
{
    private Exception? _lastError;
    private Accepted<MemoryKey>? _lastAccepted;
    private MemoryKey? _expectedKey;

    [When(@"""(.*)"" remembers note ""(.*)"" with text ""(.*)"" in namespace ""(.*)"" on memory ""(.*)""")]
    public Task RememberNote(string principal, string key, string text, string @namespace, string name)
        => Capture(principal, new MemoryKey(@namespace, key),
            () => Memory(name).Remember(new Remember(CommandId.New(), @namespace, key, text, [], null)));

    [When(@"""(.*)"" forgets note ""(.*)"" in namespace ""(.*)"" on memory ""(.*)""")]
    public Task ForgetNote(string principal, string key, string @namespace, string name)
        => Capture(principal, new MemoryKey(@namespace, key),
            () => Memory(name).Forget(new Forget(CommandId.New(), @namespace, key)));

    [Then("the memory command is accepted")]
    public void CommandAccepted()
    {
        Assert.Null(_lastError);
        Assert.NotNull(_lastAccepted);
        Assert.Equal(_expectedKey, _lastAccepted.Receipt);
        Assert.NotEqual(default, _lastAccepted.Work);
    }

    [Then(@"the memory command fails with ""(.*)""")]
    public void CommandFails(string reason)
    {
        Assert.NotNull(_lastError);
        var error = Assert.IsType<CommandRejectedException>(BrainSteps.Flatten(_lastError));
        Assert.Contains(reason, error.Reason, StringComparison.Ordinal);
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until memory ""(.*)"" recalls key ""(.*)"" with text ""(.*)"" for query ""(.*)"" in namespace ""(.*)""")]
    public async Task WaitForNote(string principal, int seconds, string name, string key, string text, string query, string @namespace)
    {
        var result = await WaitForRecall(principal, seconds, name, query, @namespace,
            result => result.Matches.Any(match => match.Key == key && match.Text == text));
        Assert.Contains(result.Matches, match => match.Key == key && match.Text == text);
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until memory ""(.*)"" recalls no matches for query ""(.*)"" in namespace ""(.*)""")]
    public async Task WaitForNoMatches(string principal, int seconds, string name, string query, string @namespace)
    {
        var result = await WaitForRecall(principal, seconds, name, query, @namespace, result => result.Matches.Count == 0);
        Assert.Empty(result.Matches);
    }

    private async Task<RecallResult> WaitForRecall(
        string principal, int seconds, string name, string query, string @namespace, Func<RecallResult, bool> satisfied)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var memory = Memory(name);
        var recall = new Recall(@namespace, query, 10, []);
        var result = await memory.Recall(recall);
        while (!satisfied(result) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            result = await memory.Recall(recall);
        }

        return result;
    }

    private async Task Capture(string principal, MemoryKey key, Func<Task<Accepted<MemoryKey>>> call)
    {
        _lastError = null;
        _lastAccepted = null;
        _expectedKey = key;
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        try
        {
            _lastAccepted = await call();
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }

    private IMemory Memory(string name)
        => world.Brain.Grains.GetGrain<IMemory>(new NeuronId("memory", name).ToGrainId());
}
