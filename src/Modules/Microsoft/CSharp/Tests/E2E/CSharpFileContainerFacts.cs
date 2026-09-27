using DigitalBrain.Core;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using ITimer = DigitalBrain.Time.Timers.ITimer;

namespace DigitalBrain.Tests;

// Needs a Docker daemon with Linux containers: the script really runs in the .NET SDK image against this silo.
public sealed class CSharpFileContainerFacts
{
    private const string Source = """
        #:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
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

    [Fact]
    public async Task ScriptOperatesNeuronsFromAContainerAndExitsCleanly()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(6));
        var ct = deadline.Token;
        var builder = new TestClusterBuilder(1);
        builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        builder.AddSiloBuilderConfigurator<ContainerSilo>();
        await using var cluster = builder.Build();
        await cluster.DeployAsync(ct);
        var timerId = "csharp-e2e-" + Guid.NewGuid().ToString("N")[..8];
        var file = cluster.Client.GetGrain<ICSharpFile>("e2e/" + timerId);
        try
        {
            await file.Write(Source, ct);
            await file.Configure(new Dictionary<string, string> { ["TimerId"] = timerId }, ct);
            var started = await file.Start(ct);
            Assert.Equal(CSharpFileStatus.Running, started.Status);

            await Logs(file, "script ready", ct);
            await cluster.Client.GetGrain<ITimer>(timerId).Start(TimeSpan.Zero);
            await Logs(file, "script tick " + timerId, ct);

            var exited = await Until(file, snapshot => snapshot.Status == CSharpFileStatus.Exited, ct);
            Assert.Equal(0, exited.ExitCode);
        }
        finally
        {
            await file.Delete(CancellationToken.None);
        }
        Assert.Equal(CSharpFileStatus.Stopped, (await file.Read(ct)).Status);
    }

    [Fact]
    public async Task ASourceThatDoesNotCompileSettlesAsExitedWithTheCompilerOutput()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        var ct = deadline.Token;
        var builder = new TestClusterBuilder(1);
        builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        builder.AddSiloBuilderConfigurator<ContainerSilo>();
        await using var cluster = builder.Build();
        await cluster.DeployAsync(ct);
        var file = cluster.Client.GetGrain<ICSharpFile>("e2e/broken-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await file.Write("Console.WriteLine(undefinedName);", ct);
            await file.Start(ct);

            var exited = await Until(file, snapshot => snapshot.Status == CSharpFileStatus.Exited, ct);

            Assert.NotEqual(0, exited.ExitCode);
            Assert.Contains("error CS0103", await file.ReadLogs(500, ct), StringComparison.Ordinal);
        }
        finally
        {
            await file.Delete(CancellationToken.None);
        }
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

    public sealed class ContainerSilo : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddDigitalBrain().AddTime();
            silo.AddMemoryGrainStorage("Default");
            silo.UseInMemoryReminderService();
            new CSharpModule().Configure(silo);
        }
    }
}
