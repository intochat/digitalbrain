using System.Threading.Channels;
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;

namespace Ino.Domains.Travel.Tests.Storyboard;

// Captures every BrainPulse emitted by BrainPulseHub during a scenario and
// projects each to a (fromGrain, toGrain, method) triple for assertion.
// One instance per scenario — dispose cancels the subscription.
//
// IMPORTANT: BrainTraceFilter always sets FromGrain = "" (Orleans' RuntimeContext
// is internal). Assertions on "Cortex→PlanTrip" style directionality are
// therefore downgraded to "PlanTrip was called" (ToGrain contains the grain type
// substring). Slice 2 assertion plan is recorded in TokyoSteps.cs TODOs.
public sealed class StoryboardCapture : IAsyncDisposable
{
    private readonly CancellationTokenSource subscriptionCts = new();
    private readonly List<CapturedFire> fires = new();
    private readonly Task pumpTask;

    public StoryboardCapture(BrainPulseHub hub)
    {
        var reader = hub.Subscribe(subscriptionCts.Token);
        pumpTask = PumpAsync(reader, subscriptionCts.Token);
    }

    public IReadOnlyList<CapturedFire> Fires
    {
        get { lock (fires) return fires.ToArray(); }
    }

    // Waits until a pulse is captured whose ToGrain contains toGrainTypeSubstring
    // (case-insensitive). FromGrain is always empty so we match on ToGrain only.
    public async Task WaitForCallAsync(string toGrainTypeSubstring, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (fires)
            {
                if (fires.Any(f => f.ToGrain.Contains(toGrainTypeSubstring, StringComparison.OrdinalIgnoreCase)))
                    return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"No captured pulse matched ToGrain~'{toGrainTypeSubstring}' within {timeout}." +
            $" Captured so far: {DescribeFires()}");
    }

    public string DescribeFires()
    {
        lock (fires)
        {
            if (fires.Count == 0) return "(none)";
            return string.Join(", ", fires.Select(f =>
                $"[from={f.FromGrain}|to={f.ToGrain}|{f.Method}]"));
        }
    }

    private async Task PumpAsync(ChannelReader<BrainPulse> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var pulse in reader.ReadAllAsync(ct))
                lock (fires) fires.Add(CapturedFire.From(pulse));
        }
        catch (OperationCanceledException) { /* expected on dispose */ }
    }

    public async ValueTask DisposeAsync()
    {
        subscriptionCts.Cancel();
        try { await pumpTask; } catch { /* ignore */ }
        subscriptionCts.Dispose();
    }
}

public sealed record CapturedFire(string FromGrain, string ToGrain, string Method)
{
    public static CapturedFire From(BrainPulse pulse) =>
        new(
            FromGrain: pulse.FromGrain ?? string.Empty,
            ToGrain:   pulse.ToGrain   ?? string.Empty,
            Method:    pulse.MethodName ?? string.Empty);
}
