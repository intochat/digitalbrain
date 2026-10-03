using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class CSharpTriggerFacts
{
    [Fact]
    public async Task AnArmedFileRunsOncePerMatchingSignalAndHandsTheSignalToTheScript()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Armed(brain, "workspace/on-ping", pinger, ct);

        var armed = await file.Read(ct);
        Assert.True(armed.ShouldRun);
        Assert.Equal(nameof(Pinged), armed.Trigger!.Signal);
        Assert.Equal(0, sandbox.Started);

        await pinger.Ping(7);
        await Eventually(() => sandbox.Started == 1, ct);
        await pinger.Ping(8);
        await Eventually(() => sandbox.Started == 2, ct);

        var runs = sandbox.Requests.Where(FakeSandbox.IsStart).ToArray();
        Assert.Equal("""{"number":7}""", runs[0].Body!["environment"]!["CSharpFile__Trigger"]!.GetValue<string>());
        Assert.Equal("""{"number":8}""", runs[1].Body!["environment"]!["CSharpFile__Trigger"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFileWhoseTriggeredRunsKeepFailingDisarms()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Armed(brain, "workspace/failing", pinger, ct);

        for (var run = 1; run <= CSharpFileNeuron.MaximumFailures; run++)
        {
            await pinger.Ping(run);
            await Eventually(() => sandbox.Started == run, ct);
            sandbox.ExitLatest(1);
        }
        await pinger.Ping(0);
        await Eventually(async () => !(await file.Read(ct)).ShouldRun, ct);

        Assert.Equal(CSharpFileNeuron.MaximumFailures, sandbox.Started);
        Assert.Equal(CSharpFileNeuron.MaximumFailures, (await file.Read(ct)).Failures);
    }

    [Fact]
    public async Task StoppingDisarms()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Armed(brain, "workspace/disarmed", pinger, ct);

        await file.Stop(ct);
        await pinger.Ping(1);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);

        Assert.Equal(0, sandbox.Started);
        Assert.False((await file.Read(ct)).ShouldRun);
    }

    [Fact]
    public async Task AnArmedFileStillFiresAfterItsActivationIsCollected()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Armed(brain, "workspace/collected", pinger, ct);

        await brain.DeactivateAsync(file, ct);
        await pinger.Ping(1);

        await Eventually(() => sandbox.Started == 1, ct);
    }

    [Fact]
    public async Task RejectsATriggerThatIsNotANeuronId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Brain(new FakeSandbox(), ct);
        var file = brain.Get<ICSharpFile>("workspace/bad-trigger");
        await file.Write("Console.WriteLine(1);", ct);

        await Assert.ThrowsAsync<ArgumentException>(() => file.Arm(new("not a grain id", nameof(Pinged)), ct));
    }

    private static async Task<ICSharpFile> Armed(ModuleBrain brain, string id, IPinger source, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(id);
        await file.Write("Console.WriteLine(1);", ct);
        await file.Arm(new(source.GetGrainId().ToString(), nameof(Pinged)), ct);
        return file;
    }

    // Triggered runs start one-way, after the publishing call has returned.
    private static Task Eventually(Func<bool> condition, CancellationToken ct) => Eventually(() => Task.FromResult(condition()), ct);

    private static async Task Eventually(Func<Task<bool>> condition, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await condition()) { await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token); }
    }

    private static Task<ModuleBrain> Brain(FakeSandbox sandbox, CancellationToken ct) => SandboxBrain.StartAsync(sandbox, ct);
}
