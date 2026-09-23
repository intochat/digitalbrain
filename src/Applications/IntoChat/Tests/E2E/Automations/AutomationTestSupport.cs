using System.Collections.Concurrent;
using DigitalBrain.Automations;
using Orleans.Runtime;
using Orleans.Storage;

namespace IntoChat.Tests.E2E.Automations;

// The test cluster restarts a silo with a fresh DI container, so in-memory providers lose state.
// These process-wide providers keep grain state and the reminder registration across that restart,
// which is what a durable production store does.
internal sealed class SharedGrainStorage : IGrainStorage
{
    public static SharedGrainStorage Instance { get; } = new();

    private readonly Dictionary<(string State, GrainId Grain), object> state = new();
    private readonly Lock gate = new();

    public static void Reset() => Instance.state.Clear();

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        lock (gate)
        {
            if (state.TryGetValue((stateName, grainId), out var stored))
            {
                grainState.State = (T)stored;
                grainState.RecordExists = true;
            }
        }
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        lock (gate)
        {
            state[(stateName, grainId)] = grainState.State!;
            grainState.ETag = Guid.NewGuid().ToString();
            grainState.RecordExists = true;
        }
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        lock (gate)
        {
            state.Remove((stateName, grainId));
            grainState.RecordExists = false;
        }
        return Task.CompletedTask;
    }
}

internal sealed class SharedReminderTable : IReminderTable
{
    public static SharedReminderTable Instance { get; } = new();

    private readonly Dictionary<GrainId, Dictionary<string, ReminderEntry>> rows = new();
    private readonly Lock gate = new();

    public static void Reset() => Instance.rows.Clear();

    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
        => ReadRow(grainId, reminderName, CancellationToken.None);

    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ReminderEntry? entry = rows.TryGetValue(grainId, out var row) && row.TryGetValue(reminderName, out var found) ? found : null;
            return Task.FromResult(entry);
        }
    }

    public Task<ReminderTableData> ReadRows(GrainId grainId)
        => ReadRows(grainId, CancellationToken.None);

    public Task<ReminderTableData> ReadRows(GrainId grainId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var list = rows.TryGetValue(grainId, out var row) ? row.Values.ToList() : [];
            return Task.FromResult(new ReminderTableData(list));
        }
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
        => ReadRows(begin, end, CancellationToken.None);

    public Task<ReminderTableData> ReadRows(uint begin, uint end, CancellationToken cancellationToken)
    {
        _ = (begin, end);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(new ReminderTableData(rows.Values.SelectMany(row => row.Values).ToList()));
        }
    }

    public Task<string?> UpsertRow(ReminderEntry entry)
        => UpsertRow(entry, CancellationToken.None);

    public Task<string?> UpsertRow(ReminderEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            entry.ETag = Guid.NewGuid().ToString();
            if (!rows.TryGetValue(entry.GrainId, out var row)) { rows[entry.GrainId] = row = new(); }
            row[entry.ReminderName] = entry;
            return Task.FromResult<string?>(entry.ETag);
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
        => RemoveRow(grainId, reminderName, eTag, CancellationToken.None);

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (rows.TryGetValue(grainId, out var row) && row.TryGetValue(reminderName, out var entry) && entry.ETag == eTag)
            {
                if (row.Count > 1) { row.Remove(reminderName); } else { rows.Remove(grainId); }
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task TestOnlyClearTable()
        => TestOnlyClearTable(CancellationToken.None);

    public Task TestOnlyClearTable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return Task.CompletedTask;
    }
}

internal sealed class PaidStepInvoker : IAutomationActionInvoker
{
    private readonly ConcurrentQueue<string> intentIds = new();

    public IReadOnlyCollection<string> IntentIds => intentIds;

    public Task<AutomationActionResult> InvokeAsync(AutomationDefinition definition, AutomationAction action, string intentId, CancellationToken cancellationToken = default)
    {
        intentIds.Enqueue(intentId);
        return Task.FromResult(new AutomationActionResult { Succeeded = true, ActualCompute = action.EstimatedCompute });
    }
}

internal sealed class FixedAutomationCatalog : IAutomationActionCatalog
{
    public Task<AutomationOperation?> FindAsync(string appId, string operation, CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationOperation?>(new AutomationOperation
        {
            AppId = appId,
            Operation = operation,
            ReadOnly = false,
            EstimatedCompute = 5m,
        });
}

internal sealed class NullAutomationJournal : IAutomationJournal
{
    public Task RecordRunAsync(AutomationDefinition definition, JobRun run, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
