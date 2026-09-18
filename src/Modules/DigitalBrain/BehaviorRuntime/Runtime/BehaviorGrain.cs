using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Behavior;
using Orleans.Runtime;

namespace DigitalBrain.Core.Behavior;

[GrainType("behavior")]
internal sealed class BehaviorGrain(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorSnapshot> state,
    [PersistentState("runs", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorRunBook> runs)
    : Grain, IBehavior
{
    public Task<BehaviorSnapshot> Read() => Task.FromResult(View());

    public async Task<BehaviorSnapshot> Write(BehaviorSnapshot snapshot, long? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = View();
        if (expectedVersion is { } expected && expected != current.Version)
        {
            throw new InvalidOperationException($"Version conflict: expected {expected}, current {current.Version}. Read the behavior and retry.");
        }

        if (snapshot.Id != this.GetPrimaryKeyString())
        {
            throw new ArgumentException("Behavior id must match its grain key.");
        }

        state.State = snapshot with { Runs = Ids() };
        await state.WriteStateAsync().ConfigureAwait(true);
        return View();
    }

    public Task<BehaviorRunSnapshot?> ReadRun(string runId)
        => Task.FromResult(History().LastOrDefault(run => run.RunId == runId));

    public async Task<BehaviorRunSnapshot> RecordRun(BehaviorRunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var history = History().ToList();
        var index = history.FindIndex(item => item.RunId == run.RunId);
        if (index >= 0)
        {
            var existing = history[index];
            if (existing.Status is "Cancelled"
                || existing.Status is "Completed" or "Filtered" or "Failed" && run.Status == "Running")
            {
                return existing;
            }

            history[index] = run;
        }
        else
        {
            history.Add(run);
        }

        if (history.Count > 100)
        {
            history.RemoveRange(0, history.Count - 100);
        }

        runs.State = new(history);
        await runs.WriteStateAsync().ConfigureAwait(true);
        var snapshot = View();
        state.State = snapshot with { Runs = [.. history.Select(item => item.RunId)] };
        await state.WriteStateAsync().ConfigureAwait(true);
        return run;
    }

    private BehaviorSnapshot View()
    {
        var snapshot = state.RecordExists
            ? state.State
            : new(this.GetPrimaryKeyString(), 0, false, null, [], []);
        return snapshot with { Runs = Ids() };
    }

    private IReadOnlyList<BehaviorRunSnapshot> History()
        => runs.RecordExists ? runs.State.Items : [];

    private IReadOnlyList<string> Ids() => [.. History().Select(run => run.RunId)];
}

[GenerateSerializer]
internal sealed record BehaviorRunBook([property: Id(0)] IReadOnlyList<BehaviorRunSnapshot> Items);
