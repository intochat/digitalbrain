using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Behavior;

internal sealed class LocalBehaviorExecutor(IOptions<BehaviorOptions> options) : IBehaviorExecutor, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();

    public Task<BehaviorExecution> StartAsync(BehaviorLaunch launch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Gateways) || string.IsNullOrWhiteSpace(settings.ClusterId) || string.IsNullOrWhiteSpace(settings.ServiceId))
        { throw new InvalidOperationException("Explicit behavior brain connection settings are required."); }
        BehaviorProgramStore.ValidateConfiguration(launch.ConfigurationJson);
        var pipeName = "brain-behavior-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BRAIN_CONTROL_PIPE"] = pipeName,
            ["BRAIN_CONTROL_TOKEN"] = token,
            ["BRAIN_GENERATION"] = launch.GenerationId.ToString(),
            ["Gateways"] = settings.Gateways,
            ["ClusterId"] = settings.ClusterId,
            ["ServiceId"] = settings.ServiceId,
            ["BRAIN_HEARTBEAT_MS"] = settings.HeartbeatInterval.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["BRAIN_HEARTBEAT_LOSS_MS"] = settings.HeartbeatLossTimeout.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        // The worker continues the launching trace: without this a behavior's grain calls start a
        // brand-new root trace and the intent cannot be followed through the child process.
        if (System.Diagnostics.Activity.Current is { } activity)
        {
            environment["TRACEPARENT"] = "00-" + activity.TraceId.ToHexString() + "-" + activity.SpanId.ToHexString()
                + "-" + (activity.ActivityTraceFlags.HasFlag(System.Diagnostics.ActivityTraceFlags.Recorded) ? "01" : "00");
            if (activity.TraceStateString is { Length: > 0 } traceState) { environment["TRACESTATE"] = traceState; }
        }
        using var config = JsonDocument.Parse(launch.ConfigurationJson);
        foreach (var item in config.RootElement.EnumerateObject()) { environment.Add(item.Name, item.Value.GetString()!); }
        WindowsContainedProcess child;
        try
        {
            child = WindowsContainedProcess.Start(settings.DotnetPath,
                [Path.Combine(launch.Artifact.LaunchDirectory, launch.Artifact.EntryAssembly)], launch.Artifact.LaunchDirectory, environment);
        }
        catch { pipe.Dispose(); throw; }
        var worker = new Worker(child, pipe);
        if (!_workers.TryAdd(launch.GenerationId, worker)) { worker.Dispose(); throw new InvalidOperationException("Generation already exists."); }
        var completion = Monitor(launch, worker, token);
        return Task.FromResult(new BehaviorExecution(launch.GenerationId, child.Process.Id, completion));
    }

    public async Task StopAsync(Guid generationId, CancellationToken cancellationToken)
    {
        if (!_workers.TryGetValue(generationId, out var worker)) { return; }
        worker.StopRequested = true;
        try { await worker.Finished.Task.WaitAsync(options.Value.StopTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { worker.Child.Terminate(); }
        finally
        {
            worker.Child.Terminate();
            await worker.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<BehaviorExit> Monitor(BehaviorLaunch launch, Worker worker, string token)
    {
        var output = Capture(worker.Child.Output, "stdout", launch, worker.Lifetime.Token);
        var error = Capture(worker.Child.Error, "stderr", launch, worker.Lifetime.Token);
        var control = Control(launch, worker, token);
        string? failure = null;
        var code = -1;
        try
        {
            var exited = worker.Child.Process.WaitForExitAsync(worker.Lifetime.Token);
            if (await Task.WhenAny(exited, control).ConfigureAwait(false) == control && !exited.IsCompleted)
            {
                try { await control.ConfigureAwait(false); }
                catch (EndOfStreamException)
                {
                    // The app closes its pipe while unwinding before the OS reports process exit.
                    await exited.WaitAsync(options.Value.StopTimeout).ConfigureAwait(false);
                }
                if (!exited.IsCompleted) { await exited.WaitAsync(options.Value.StopTimeout).ConfigureAwait(false); }
            }
            await exited.ConfigureAwait(false);
            code = worker.Child.Process.ExitCode;
        }
        catch (Exception exception) { failure = exception.Message; }
        finally
        {
            worker.Child.Terminate();
            try { await worker.Child.Process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception)
            {
                await worker.Lifetime.CancelAsync().ConfigureAwait(false);
                worker.Finished.TrySetException(exception);
                // Retain the worker and fault completion: a replacement must never start without confirmed exit.
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
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(worker.Lifetime.Token);
        startup.CancelAfter(options.Value.StartupTimeout);
        await worker.Pipe.WaitForConnectionAsync(startup.Token).ConfigureAwait(false);
        using var reader = new StreamReader(worker.Pipe, leaveOpen: true);
        using var writer = new StreamWriter(worker.Pipe, leaveOpen: true) { AutoFlush = true };
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

    public void Dispose() { foreach (var worker in _workers.Values) { worker.Child.Terminate(); } }

    private sealed class Worker(WindowsContainedProcess child, NamedPipeServerStream pipe) : IDisposable
    {
        public WindowsContainedProcess Child { get; } = child;
        public NamedPipeServerStream Pipe { get; } = pipe;
        public CancellationTokenSource Lifetime { get; } = new();
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool StopRequested;
        public void Dispose() { Child.Dispose(); Pipe.Dispose(); Lifetime.Dispose(); }
    }
}