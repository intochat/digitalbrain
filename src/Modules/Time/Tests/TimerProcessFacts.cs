using System.Collections.Concurrent;
using System.Diagnostics;
using DigitalBrain.Time;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using Orleans.TestingHost;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class TimerProcessFacts
{
    [Fact]
    public async Task FileBasedProductionClientReportsOneRealTimerAndExits()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new TestClusterBuilder(1);
        builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        builder.AddSiloBuilderConfigurator<ProcessSilo>();
        await using var cluster = builder.Build();
        await cluster.DeployAsync(ct);
        var endpoint = cluster.GetSiloServiceProvider().GetRequiredService<IOptions<EndpointOptions>>().Value;
        var gateway = new UriBuilder("gwy.tcp", endpoint.AdvertisedIPAddress.ToString(), endpoint.GatewayPort, "0").Uri;
        var root = FindRoot();
        var output = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "run", "--file", "src/Behaviors/timer-report.cs", "-p:CodeGraphRefresh=false", "--",
            "--Smoke", "true", "--TimerId", "process", "--ClusterId", cluster.Options.ClusterId,
            "--ServiceId", cluster.Options.ServiceId, "--Gateways", gateway.ToString() })
        { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        void OnLine(object sender, DataReceivedEventArgs args)
        {
            if (args.Data is not { } line) { return; }
            output.Enqueue(line);
            if (line.Contains("SubscriptionReady", StringComparison.Ordinal)) { ready.TrySetResult(); }
        }
        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var exit = process.WaitForExitAsync(ct);
            var winner = await Task.WhenAny(ready.Task, exit).WaitAsync(TimeSpan.FromSeconds(90), ct);
            Assert.True(winner == ready.Task, string.Join(Environment.NewLine, output));
            var scheduled = await cluster.Client.GetGrain<ITimer>("process").Schedule(1, "tea");
            await exit.WaitAsync(TimeSpan.FromSeconds(90), ct);
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, string.Join(Environment.NewLine, output));
            Assert.Single(output, line => line.StartsWith($"TimerElapsed process {scheduled.Generation} ", StringComparison.Ordinal));
        }
        catch (TimeoutException error)
        { throw new TimeoutException(string.Join(Environment.NewLine, output), error); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
        }
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.Foundation.slnx")))
        { directory = directory.Parent; }
        return directory?.FullName ?? throw new InvalidOperationException("Cannot find the foundation checkout.");
    }
    public sealed class ProcessSilo : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddDigitalBrain().AddTime();
            silo.AddMemoryGrainStorage("Default");
            silo.UseInMemoryReminderService();
        }
    }
}

