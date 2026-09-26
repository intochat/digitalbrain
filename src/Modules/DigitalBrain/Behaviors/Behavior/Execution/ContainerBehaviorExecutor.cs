using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Behavior;

internal sealed class ContainerBehaviorExecutor(IOptions<BehaviorOptions> options) : IBehaviorExecutor, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();

    public async Task<BehaviorExecution> StartAsync(BehaviorLaunch launch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SandboxImage))
        { throw new InvalidOperationException("Behavior sandbox image is required."); }
        if (string.IsNullOrWhiteSpace(settings.Gateways) || string.IsNullOrWhiteSpace(settings.ClusterId) || string.IsNullOrWhiteSpace(settings.ServiceId))
        { throw new InvalidOperationException("Explicit behavior brain connection settings are required."); }
        BehaviorProgramStore.ValidateConfiguration(launch.ConfigurationJson);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var name = "digitalbrain-behavior-" + launch.GenerationId.ToString("N");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BRAIN_CONTROL_PIPE"] = BehaviorSandboxControl.Listen,
            ["BRAIN_CONTROL_TOKEN"] = token,
            ["BRAIN_GENERATION"] = launch.GenerationId.ToString(),
            ["Gateways"] = settings.Gateways,
            ["ClusterId"] = settings.ClusterId,
            ["ServiceId"] = settings.ServiceId,
            ["BRAIN_HEARTBEAT_MS"] = settings.HeartbeatInterval.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["BRAIN_HEARTBEAT_LOSS_MS"] = settings.HeartbeatLossTimeout.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (Activity.Current is { } activity)
        {
            environment["TRACEPARENT"] = "00-" + activity.TraceId.ToHexString() + "-" + activity.SpanId.ToHexString()
                + "-" + (activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00");
            if (activity.TraceStateString is { Length: > 0 } traceState) { environment["TRACESTATE"] = traceState; }
        }
        using var config = JsonDocument.Parse(launch.ConfigurationJson);
        foreach (var item in config.RootElement.EnumerateObject()) { environment.Add(item.Name, item.Value.GetString()!); }
        var port = FreePort();
        var arguments = BehaviorSandbox.Arguments(new BehaviorSandboxRequest(
            settings.SandboxImage, name, launch.Artifact.LaunchDirectory, launch.Artifact.EntryAssembly, port, environment));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(settings.DockerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments) { process.StartInfo.ArgumentList.Add(argument); }
        if (!process.Start()) { throw new InvalidOperationException("Docker did not start the behavior sandbox."); }
        var worker = new Worker(process, name, port);
        if (!_workers.TryAdd(launch.GenerationId, worker))
        {
            worker.Dispose();
            throw new InvalidOperationException("Generation already exists.");
        }
        try
        {
            worker.Stream = await ConnectAsync(port, settings.StartupTimeout, worker.Lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            Kill(worker);
            _workers.TryRemove(launch.GenerationId, out _);
            throw;
        }
        var completion = Monitor(launch, worker, token);
        return new BehaviorExecution(launch.GenerationId, process.Id, completion);
    }

    public async Task StopAsync(Guid generationId, CancellationToken cancellationToken)
    {
        if (!_workers.TryGetValue(generationId, out var worker)) { return; }
        worker.StopRequested = true;
        try { await worker.Finished.Task.WaitAsync(options.Value.StopTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { Kill(worker); }
        finally
        {
            Kill(worker);
            await worker.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<BehaviorExit> Monitor(BehaviorLaunch launch, Worker worker, string token)
    {
        var output = Capture(worker.Process.StandardOutput, "stdout", launch, worker.Lifetime.Token);
        var error = Capture(worker.Process.StandardError, "stderr", launch, worker.Lifetime.Token);
        var control = Control(launch, worker, token);
        string? failure = null;
        var code = -1;
        try
        {
            var exited = worker.Process.WaitForExitAsync(worker.Lifetime.Token);
            if (await Task.WhenAny(exited, control).ConfigureAwait(false) == control && !exited.IsCompleted)
            {
                try { await control.ConfigureAwait(false); }
                catch (EndOfStreamException) { await exited.WaitAsync(options.Value.StopTimeout).ConfigureAwait(false); }
                if (!exited.IsCompleted) { await exited.WaitAsync(options.Value.StopTimeout).ConfigureAwait(false); }
            }
            await exited.ConfigureAwait(false);
            code = worker.Process.ExitCode;
        }
        catch (Exception exception) { failure = exception.Message; }
        finally
        {
            Kill(worker);
            try { await worker.Process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception)
            {
                await worker.Lifetime.CancelAsync().ConfigureAwait(false);
                worker.Finished.TrySetException(exception);
                throw;
            }
            await worker.Lifetime.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(output, error, control).ConfigureAwait(false); }
            catch (Exception exception) { if (!worker.StopRequested && failure is null && code != 0) { failure = exception.Message; } }
            _workers.TryRemove(launch.GenerationId, out _);
            worker.Finished.TrySetResult();
            worker.Dispose();
        }
        return new(code, failure);
    }

    private async Task Control(BehaviorLaunch launch, Worker worker, string token)
    {
        var stream = worker.Stream ?? throw new InvalidOperationException("Sandbox control stream is missing.");
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(worker.Lifetime.Token);
        startup.CancelAfter(options.Value.StartupTimeout);
        var hello = JsonSerializer.Deserialize<BehaviorControlMessage>(await BehaviorApp.ReadControlLineAsync(reader, startup.Token).ConfigureAwait(false));
        if (hello is null || hello.Version != 1 || hello.GenerationId != launch.GenerationId || hello.Kind != "hello" || hello.Token != token)
        { throw new IOException("Invalid worker handshake."); }
        long received = hello.Sequence, sent = 0;
        var ready = false;
        var deadlineAt = DateTimeOffset.UtcNow + options.Value.StartupTimeout;
        while (!worker.Lifetime.IsCancellationRequested)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new BehaviorControlMessage(1, launch.GenerationId, ++sent, worker.StopRequested ? "stop" : "heartbeat")).AsMemory(), worker.Lifetime.Token).ConfigureAwait(false);
            if (worker.StopRequested) { return; }
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(worker.Lifetime.Token);
            heartbeat.CancelAfter(options.Value.HeartbeatLossTimeout);
            var message = JsonSerializer.Deserialize<BehaviorControlMessage>(await BehaviorApp.ReadControlLineAsync(reader, heartbeat.Token).ConfigureAwait(false));
            if (message is null || message.Version != 1 || message.GenerationId != launch.GenerationId || message.Sequence <= received || message.Kind != "heartbeat")
            { throw new IOException("Invalid worker heartbeat."); }
            received = message.Sequence;
            if (message.Ready != ready) { ready = message.Ready; await launch.Readiness(ready).ConfigureAwait(false); }
            if (!ready && DateTimeOffset.UtcNow >= deadlineAt) { throw new TimeoutException("Behavior did not become ready before its startup deadline."); }
        }
    }

    private static async Task Capture(StreamReader reader, string stream, BehaviorLaunch launch, CancellationToken ct)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        { await launch.Log(stream, new string(buffer, 0, read)).ConfigureAwait(false); }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<NetworkStream> ConnectAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
                return client.GetStream();
            }
            catch (SocketException) when (!deadline.IsCancellationRequested)
            {
                client.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token).ConfigureAwait(false);
            }
        }
    }

    private void Kill(Worker worker)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo(options.Value.DockerPath, $"kill {worker.Name}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            kill?.WaitForExit(5_000);
        }
        catch (Exception) { }
        try { if (!worker.Process.HasExited) { worker.Process.Kill(entireProcessTree: true); } }
        catch (Exception) { }
    }

    public void Dispose() { foreach (var worker in _workers.Values) { Kill(worker); } }

    private sealed class Worker(Process process, string name, int port) : IDisposable
    {
        public Process Process { get; } = process;
        public string Name { get; } = name;
        public int Port { get; } = port;
        public NetworkStream? Stream { get; set; }
        public CancellationTokenSource Lifetime { get; } = new();
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool StopRequested;
        public void Dispose() { Stream?.Dispose(); Process.Dispose(); Lifetime.Dispose(); }
    }
}
