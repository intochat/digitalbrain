using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace IntoChat;

internal static class SessionStream
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(25);

    public static async Task RunAsync<TEvent>(
        HttpContext http,
        INeuron neuron,
        NeuronId neuronId,
        JournalKind kind,
        long afterSequence,
        Func<SignalDelivery, TEvent?> project,
        string eventName,
        Func<CancellationToken, Task<object?>> readResetState,
        SessionStreamOptions options,
        CancellationToken cancellationToken,
        bool resetOnConnect = false) where TEvent : class
    {
        _ = neuronId;
        await RunLoopAsync(http, neuron, kind, afterSequence, async (read, reset, token) =>
        {
            if (reset)
            {
                var state = new StreamReset(read.ResumeSequence, await readResetState(token).ConfigureAwait(false));
                await SseWriter.WriteAsync(http.Response, "reset", state, null, token).ConfigureAwait(false);
                return true;
            }

            var written = false;
            for (var index = 0; index < read.Delta.Count; index++)
            {
                if (project(read.Delta[index]) is { } payload)
                {
                    var position = read.ResumeSequence - read.Delta.Count + index + 1;
                    await SseWriter.WriteAsync(http.Response, eventName, payload, position, token).ConfigureAwait(false);
                    written = true;
                }
            }

            return written;
        }, options, cancellationToken, resetOnConnect).ConfigureAwait(false);
    }

    public static async Task RunSnapshotAsync<TSnapshot>(
        HttpContext http,
        INeuron neuron,
        NeuronId neuronId,
        JournalKind kind,
        long afterSequence,
        Func<CancellationToken, Task<TSnapshot>> readSnapshot,
        string eventName,
        SessionStreamOptions options,
        CancellationToken cancellationToken)
    {
        _ = neuronId;
        await RunLoopAsync(http, neuron, kind, afterSequence, async (read, reset, token) =>
        {
            if (!reset && read.Delta.Count == 0)
            {
                return false;
            }

            var snapshot = await readSnapshot(token).ConfigureAwait(false);
            await SseWriter.WriteAsync(http.Response, eventName, snapshot, null, token).ConfigureAwait(false);
            return true;
        }, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunLoopAsync(
        HttpContext http,
        INeuron neuron,
        JournalKind kind,
        long afterSequence,
        Func<JournalRead, bool, CancellationToken, Task<bool>> writePass,
        SessionStreamOptions options,
        CancellationToken cancellationToken,
        bool resetOnConnect = false)
    {
        if (afterSequence < 0)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try
        {
            await SseWriter.StartAsync(http.Response, cancellationToken).ConfigureAwait(false);
            var cursor = afterSequence;
            var idle = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await neuron.ReadJournal(kind, cursor).WaitAsync(cancellationToken).ConfigureAwait(false);
                var reset = resetOnConnect || read.Gap || cursor > read.ResumeSequence;
                resetOnConnect = false;
                var written = await writePass(read, reset, cancellationToken).ConfigureAwait(false);
                cursor = read.ResumeSequence;
                if (written)
                {
                    idle.Restart();
                }
                else
                {
                    await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (idle.Elapsed >= KeepAliveInterval)
                    {
                        await SseWriter.KeepAliveAsync(http.Response, cancellationToken).ConfigureAwait(false);
                        idle.Restart();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || http.RequestAborted.IsCancellationRequested)
        {
        }
    }
}

internal sealed record StreamReset(long Cursor, object? State);
