using DigitalBrain.Microsoft.Aspire;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Time;
using ITimer = DigitalBrain.Time.Timers.ITimer;

namespace DigitalBrain.Tests;

// Needs a Docker daemon with Linux containers: Aspire builds and starts the sandbox on the first run,
// and the script compiles and runs inside it against this brain.
public sealed class CSharpFileContainerFacts
{
    private const string Source = """
        #:project /brain/src/Modules/Time/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
        using DigitalBrain.Time.Timers.Signals;
        using ITimer = DigitalBrain.Time.Timers.ITimer;

        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        await using var ticks = await brain.SubscribeAsync<TimerTick>(brain.Get<ITimer>(brain.Setting("TimerId")!), brain.Stopping);
        Console.WriteLine("script ready");
        await foreach (var tick in ticks.ReadAllAsync(brain.Stopping))
        {
            Console.WriteLine($"script tick {tick.TimerId}");
            break;
        }
        """;

    private const string TriggeredSource = """
        #:project /brain/src/Modules/Time/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
        using DigitalBrain.Time.Timers.Signals;

        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var tick = brain.Trigger<TimerTick>();
        Console.WriteLine($"triggered by {tick.TimerId}");
        """;

    [Fact]
    public async Task TheSandboxStartsOnDemandRunsScriptsSideBySideAndComesBackAfterItStops()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        await using var brain = await E2ETest.Create()
            .WithModule<TimeModule>()
            .WithModule<AspireModule>()
            .WithModule<CSharpModule>(csharp => csharp.WithSandbox(RepositoryRoot()))
            .StartAsync(ct);
        var aspire = brain.Get<IAspire>(new AspireOptions().ApplicationName);
        await using var sandboxStates = await brain.Observe<ResourceStateChanged>(aspire, ct);
        var timerId = "csharp-e2e-" + Guid.NewGuid().ToString("N")[..8];
        var listener = brain.Get<ICSharpFile>("e2e/" + timerId);
        var broken = brain.Get<ICSharpFile>("e2e/broken-" + timerId);
        var triggerTimerId = "csharp-trigger-" + Guid.NewGuid().ToString("N")[..8];
        var triggered = brain.Get<ICSharpFile>("e2e/triggered-" + timerId);
        try
        {
            await listener.Write(Source, ct);
            await listener.Configure(new Dictionary<string, string> { ["TimerId"] = timerId }, ct);
            Assert.Equal(CSharpFileStatus.Running, (await listener.Start(ct)).Status);
            await sandboxStates.NextAsync(change => change.Resource == CSharpSandbox.ResourceName && change.State == "Running", ct: ct);

            await broken.Write("Console.WriteLine(undefinedName);", ct);
            await broken.Start(ct);

            await Logs(listener, "script ready", ct);
            Assert.NotEqual(0, (await Until(broken, snapshot => snapshot.ExitCode is not null, ct)).ExitCode);
            Assert.Contains("error CS0103", await broken.ReadLogs(500, ct), StringComparison.Ordinal);
            await broken.Stop(ct);

            // Losing the sandbox loses every run in it; the reconcile starts the sandbox and the listener again.
            await aspire.StopResource(CSharpSandbox.ResourceName, ct);
            await Until(listener, snapshot => snapshot.Status == CSharpFileStatus.Restarting, ct);
            await Until(listener, snapshot => snapshot.Status == CSharpFileStatus.Running, ct);
            await Logs(listener, "script ready", ct);

            await brain.Get<ITimer>(timerId).Start(TimeSpan.Zero);
            await Logs(listener, "script tick " + timerId, ct);
            var finished = await Until(listener, snapshot => snapshot.Status == CSharpFileStatus.Exited, ct);
            Assert.Equal(0, finished.ExitCode);

            // An armed file runs nothing until its trigger fires, then runs once with the signal.
            await triggered.Write(TriggeredSource, ct);
            var triggerTimer = brain.Get<ITimer>(triggerTimerId);
            Assert.Equal(CSharpFileStatus.Stopped, (await triggered.Arm(new(triggerTimer.GetGrainId().ToString(), "TimerTick"), ct)).Status);
            await triggerTimer.Start(TimeSpan.Zero);
            await Logs(triggered, "triggered by " + triggerTimerId, ct);
            var ran = await Until(triggered, snapshot => snapshot.Status == CSharpFileStatus.Exited, ct);
            Assert.Equal(0, ran.ExitCode);
            Assert.True(ran.ShouldRun);
        }
        finally
        {
            await listener.Delete(CancellationToken.None);
            await broken.Delete(CancellationToken.None);
            await triggered.Delete(CancellationToken.None);
        }
        Assert.Equal(CSharpFileStatus.Stopped, (await listener.Read(ct)).Status);
    }

    private static async Task Logs(ICSharpFile file, string expected, CancellationToken ct)
    {
        var logs = "";
        try
        {
            while (!(logs = await file.ReadLogs(500, ct)).Contains(expected, StringComparison.Ordinal))
            { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
        }
        catch (OperationCanceledException error)
        { throw new TimeoutException($"'{expected}' never appeared. Logs:{Environment.NewLine}{logs}", error); }
    }

    private static async Task<CSharpFileSnapshot> Until(ICSharpFile file, Func<CSharpFileSnapshot, bool> reached, CancellationToken ct)
    {
        while (true)
        {
            var snapshot = await file.Read(ct);
            if (reached(snapshot)) { return snapshot; }
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("The sandbox mounts the repository; run the test from inside it.");
    }
}
