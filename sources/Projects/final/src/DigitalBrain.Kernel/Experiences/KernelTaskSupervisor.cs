using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
public sealed record KernelTaskState
{
    [Id(0)]
    public Dictionary<string, KernelTask> Tasks { get; set; } = new();

    [Id(1)]
    public Dictionary<string, AlarmInfo> ActiveAlarms { get; set; } = new();
}

[GenerateSerializer]
public sealed record AlarmInfo(string Label, DateTimeOffset FiresAt);

public interface IKernelTaskSupervisor : INeuron,
    IHandle<InstallBundle>,
    IHandle<KernelTaskStarted>,
    IHandle<KernelTaskStatusChanged>,
    IHandle<KernelTaskLogAppended>,
    IHandle<InspectKernelTask>,
    IHandle<SetAlarm>,
    IHandle<DismissAlarm> { }

[GrainType("kerneltasks")]
public sealed class KernelTaskSupervisor : Neuron, IKernelTaskSupervisor, IRemindable
{
    private readonly IPersistentState<KernelTaskState> _state;
    private readonly Dictionary<string, IGrainReminder> _alarmReminders = new();
    private IReminderRegistry? _reminderRegistry;

    public KernelTaskSupervisor(
        [PersistentState("kerneltasks", "Default")] IPersistentState<KernelTaskState> state)
        : base()
    {
        _state = state;
    }

    private Dictionary<string, KernelTask> Tasks => _state.State.Tasks;

    public async Task HandleAsync(InstallBundle e, CancellationToken cancellationToken)
    {
        if (e.BundleId == "kernel-tasks" && Tasks.Count == 0)
        {
            await SeedDemoAsync(cancellationToken);
            EmitList();
        }
        return;
    }

    public async Task HandleAsync(KernelTaskStarted s, CancellationToken cancellationToken)
    {
        // New DU for status (from the union in KernelTask.cs).
        Tasks[s.TaskId] = new KernelTask(s.TaskId, s.Description, new TaskRunning(), new List<string> { "started" });
        await _state.WriteStateAsync(cancellationToken);
        EmitList();
        return;
    }

    public async Task HandleAsync(KernelTaskStatusChanged c, CancellationToken cancellationToken)
    {
        if (Tasks.TryGetValue(c.TaskId, out var t))
        {
            // DU carried directly on the synapse now (exhaustive DDD status from KernelTaskStatus union).
            Tasks[c.TaskId] = t with { Status = c.Status };
            await _state.WriteStateAsync(cancellationToken);
            EmitList();
        }
        return;
    }

    public async Task HandleAsync(KernelTaskLogAppended l, CancellationToken cancellationToken)
    {
        if (Tasks.TryGetValue(l.TaskId, out var t))
        {
            var logs = new List<string>(t.Logs) { l.Line };
            Tasks[l.TaskId] = t with { Logs = logs };
            await _state.WriteStateAsync(cancellationToken);
            EmitList();
        }
        return;
    }

    public Task HandleAsync(InspectKernelTask i, CancellationToken cancellationToken)
    {
        if (Tasks.TryGetValue(i.TaskId, out var t))
        {
            EmitDetail(i.TaskId, t);
        }
        return Task.CompletedTask;
    }

    public async Task HandleAsync(SetAlarm s, CancellationToken cancellationToken)
    {
        var id = $"alarm-{Guid.NewGuid().ToString("N")[..8]}";
        var firesAt = DateTimeOffset.UtcNow.AddMinutes(s.Minutes);

        _state.State.ActiveAlarms[id] = new AlarmInfo(s.Label, firesAt);
        await _state.WriteStateAsync(cancellationToken);

        _reminderRegistry ??= ServiceProvider.GetRequiredService<IReminderRegistry>();
        var due = TimeSpan.FromMinutes(Math.Max(0, s.Minutes));
        var rem = await _reminderRegistry.RegisterOrUpdateReminder(GrainContext.GrainId, id, due, TimeSpan.Zero);
        _alarmReminders[id] = rem;

        _ = Emit(new AlarmSet(id, firesAt, s.Label));

        // UI surface now produced by rule in kernel-tasks.ino (show card with $ substitution from this AlarmSet event)
    }

    public async Task HandleAsync(DismissAlarm d, CancellationToken cancellationToken)
    {
        _state.State.ActiveAlarms.Remove(d.AlarmId);
        await _state.WriteStateAsync(cancellationToken);

        if (_alarmReminders.TryGetValue(d.AlarmId, out var rem) && rem is not null)
        {
            _reminderRegistry ??= ServiceProvider.GetRequiredService<IReminderRegistry>();
            try { await _reminderRegistry.UnregisterReminder(GrainContext.GrainId, rem); } catch { }
            _alarmReminders.Remove(d.AlarmId);
        }
        else
        {
            try
            {
                _reminderRegistry ??= ServiceProvider.GetRequiredService<IReminderRegistry>();
                var r = await _reminderRegistry.GetReminder(GrainContext.GrainId, d.AlarmId);
                if (r is not null) await _reminderRegistry.UnregisterReminder(GrainContext.GrainId, r);
            }
            catch { }
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!_state.State.ActiveAlarms.TryGetValue(reminderName, out var info)) return;

        _state.State.ActiveAlarms.Remove(reminderName);
        await _state.WriteStateAsync();

        _reminderRegistry ??= ServiceProvider.GetRequiredService<IReminderRegistry>();
        try
        {
            var r = await _reminderRegistry.GetReminder(GrainContext.GrainId, reminderName);
            if (r is not null) await _reminderRegistry.UnregisterReminder(GrainContext.GrainId, r);
        }
        catch { }
        _alarmReminders.Remove(reminderName);

        _ = Emit(new AlarmFired(reminderName));

        // UI surface now produced by rule in kernel-tasks.ino (show card with $ substitution from this AlarmFired event)
    }

    private async Task SeedDemoAsync(CancellationToken cancellationToken)
    {
        // Using the new KernelTaskStatus union (native C# DU) for expressive/safe status instead of magic string.
        // Each case is exact; exhaustive handling possible in switches.
        // Now persisted for durable grain (switch per user request + 5 steps after deleting heavy old journals).
        Tasks["task-1"] = new KernelTask("task-1", "guide domain load", new TaskRunning(), new List<string> { "[10:00] started via domain install" });
        Tasks["task-2"] = new KernelTask("task-2", "human gate", new TaskSuspended(), new List<string> { "[10:01] ui surface emitted", "[10:02] suspended" });
        await _state.WriteStateAsync(cancellationToken);
    }

    private void EmitList()
    {
        var taskWidgets = new List<UiWidget>();
        foreach (var kv in Tasks)
        {
            var t = kv.Value;
            taskWidgets.Add(new Text($"{kv.Key} [{t.Status}]"));
            taskWidgets.Add(new Button("Inspect", new InspectKernelTask(kv.Key)));
        }
        var listColumn = new Column(taskWidgets.ToArray());
        // Kerneltasks surface now from rule in os/kernel-tasks.ino (on NeuronTelemetry KernelTasksListed or SetAlarm/AlarmFired).
        Emit(new NeuronTelemetry(Self, "KernelTasksListed", new Dictionary<string, string> { ["count"] = Tasks.Count.ToString() }));
    }

    private void EmitDetail(string taskId, KernelTask task)
    {
        var logLines = new List<UiWidget> { new Text($"Status: {task.Status}"), new Text("Logs:") };
        foreach (var logEntry in task.Logs)
        {
            logLines.Add(new Text($"  {logEntry}"));
        }
        var inner = new Column(logLines.ToArray());
        var card = new Card($"KernelTask {taskId} (detail from supervisor)", inner);
        // Detail surface removed; rule or client constructs from telemetry/Inspect.
        Emit(new NeuronTelemetry(Self, "KernelTaskDetail", new Dictionary<string, string> { ["id"] = taskId }));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await ReEmitActiveAlarmsAsync();
    }

    private async Task ReEmitActiveAlarmsAsync()
    {
        foreach (var (alarmId, alarmInfo) in _state.State.ActiveAlarms)
        {
            // Alarm surfaces now produced by rule in os/kernel-tasks.ino on: AlarmFired / SetAlarm (with $ substitution).
            await Emit(new NeuronTelemetry(Self, "ActiveAlarmReplayed", new Dictionary<string, string> { ["id"] = alarmId, ["label"] = alarmInfo.Label }));
        }
    }
}