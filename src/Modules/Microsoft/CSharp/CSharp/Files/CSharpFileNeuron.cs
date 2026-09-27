using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Microsoft.CSharp;

// Start records the intent to keep a run up; Arm records the intent to run once per trigger signal.
// A reminder reconciles either intent with the sandbox, so a file survives lost runs and silo restarts.
[GrainType("microsoft.csharp.file")]
internal sealed partial class CSharpFileNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CSharpFileState> store,
    ICSharpRunner runner,
    ScriptRunEnvironment environment,
    IReminderRegistry reminders)
    : Neuron<CSharpFileState>(store), ICSharpFile, ICSharpFileTrigger, ICSharpFileEdge, IRemindable, INeuronObserver
{
    internal const int MaximumSourceBytes = 128 * 1024;
    internal const int MaximumFailures = 5;
    internal const string ReconcileReminder = "reconcile";
    // Orleans reminders fire at most once a minute.
    internal static readonly TimeSpan ReconcilePeriod = TimeSpan.FromMinutes(1);
    private const int MaximumSettings = 64;
    private INeuron? _triggerSource;
    private IGrainTimer? _triggerRenewal;
    private string FileId => this.GetPrimaryKeyString();
    private bool IsArmed => Snapshot is { ShouldRun: true, Trigger: not null };

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        // The source's own signal may be what activated this file, so watching it now would deadlock;
        // the renewal timer's first tick watches it right after activation instead.
        if (IsArmed) { RenewTriggerWatch(dueTime: TimeSpan.Zero); }
    }

    public async Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default)
        => Describe(await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken));

    public Task Write(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes) { throw new ArgumentException($"Source exceeds {MaximumSourceBytes} bytes.", nameof(source)); }
        return Save(Snapshot with { Source = source }, new CSharpFileChanged(FileId));
    }

    public Task Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > MaximumSettings) { throw new ArgumentException($"At most {MaximumSettings} settings are allowed.", nameof(settings)); }
        if (settings.Keys.FirstOrDefault(name => !SettingName().IsMatch(name)) is { } invalid)
        { throw new ArgumentException($"Setting '{invalid}' must contain only letters, digits and underscores.", nameof(settings)); }
        return Save(Snapshot with { Settings = new(settings, StringComparer.Ordinal) }, new CSharpFileChanged(FileId));
    }

    public async Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default)
    {
        RequireSource();
        await UnwatchTriggerAsync();
        await StopCurrentRunAsync(cancellationToken);
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), ReconcileReminder, ReconcilePeriod, ReconcilePeriod);
        var owner = Caller();
        var runId = await StartRunAsync(Snapshot with { ShouldRun = true, Failures = 0, Owner = owner.Principal, OwnerContext = owner.Context, Trigger = null }, trigger: null, cancellationToken);
        return Describe(await runner.InspectAsync(owner.Principal, runId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Arm(CSharpTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        RequireSource();
        if (!GrainId.TryParse(trigger.Neuron, out _)) { throw new ArgumentException($"'{trigger.Neuron}' is not a neuron id such as time.timer/tea.", nameof(trigger)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger.Signal);
        await UnwatchTriggerAsync();
        await StopCurrentRunAsync(cancellationToken);
        var owner = Caller();
        await Save(Snapshot with { ShouldRun = true, Failures = 0, Owner = owner.Principal, OwnerContext = owner.Context, Trigger = trigger }, new CSharpFileChanged(FileId));
        await WatchTriggerAsync();
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), ReconcileReminder, ReconcilePeriod, ReconcilePeriod);
        return Describe(await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        await UnwatchTriggerAsync();
        await StopCurrentRunAsync(cancellationToken);
        var run = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken);
        await Save(Snapshot with { ShouldRun = false }, new CSharpFileChanged(FileId));
        return Describe(run);
    }

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => runner.LogsAsync(Snapshot.Owner, Snapshot.RunId, tail, cancellationToken);

    public async Task Delete(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        await UnwatchTriggerAsync();
        await StopCurrentRunAsync(cancellationToken);
        await Save(new CSharpFileState(), new CSharpFileChanged(FileId));
        DeactivateOnIdle();
    }

    Task INeuronObserver.OnSignalAsync(Signal signal)
        => IsArmed && signal.GetType().Name == Snapshot.Trigger!.Signal
            ? this.AsReference<ICSharpFileTrigger>().Fire(signal)
            : Task.CompletedTask;

    // Triggered runs are not retried: the next signal is the next attempt. A file whose runs keep
    // failing disarms instead of burning a run on every signal.
    public async Task Fire(Signal signal)
    {
        if (!IsArmed) { return; }
        var previous = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, CancellationToken.None);
        var failures = previous switch
        {
            { Status: CSharpFileStatus.Exited, ExitCode: not 0 } => Snapshot.Failures + 1,
            { Status: CSharpFileStatus.Exited } => 0,
            _ => Snapshot.Failures,
        };
        if (failures >= MaximumFailures)
        {
            await StopReconcilingAsync();
            await UnwatchTriggerAsync();
            await Save(Snapshot with { ShouldRun = false, Failures = failures }, new CSharpFileChanged(FileId));
            return;
        }
        await StartRunAsync(Snapshot with { Failures = failures }, signal, CancellationToken.None);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ReconcileReminder) { return; }
        if (!Snapshot.ShouldRun)
        {
            await StopReconcilingAsync();
            return;
        }
        if (Snapshot.Trigger is not null)
        {
            if (_triggerSource is null) { await WatchTriggerAsync(); }
            return;
        }
        var run = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, CancellationToken.None);
        switch (run.Status)
        {
            case CSharpFileStatus.Running:
                return;
            case CSharpFileStatus.Exited when run.ExitCode == 0:
                await StopReconcilingAsync();
                await Save(Snapshot with { ShouldRun = false }, new CSharpFileChanged(FileId));
                return;
            case CSharpFileStatus.Exited when Snapshot.Failures + 1 >= MaximumFailures:
                await StopReconcilingAsync();
                await Save(Snapshot with { ShouldRun = false, Failures = Snapshot.Failures + 1 }, new CSharpFileChanged(FileId));
                return;
            case CSharpFileStatus.Exited:
                await StartRunAsync(Snapshot with { Failures = Snapshot.Failures + 1 }, trigger: null, CancellationToken.None);
                return;
            default:
                // The run or its whole sandbox is gone; that is not the script's failure.
                await StartRunAsync(Snapshot, trigger: null, CancellationToken.None);
                return;
        }
    }

    private async Task<string> StartRunAsync(CSharpFileState next, Signal? trigger, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        await runner.StartAsync(new CSharpRun(next.Owner, runId, next.Source, environment.Create(FileId, runId, next.Settings, trigger)), cancellationToken);
        await Save(next with { RunId = runId }, new CSharpFileChanged(FileId));
        return runId;
    }

    private async Task WatchTriggerAsync()
    {
        var source = RenewTriggerWatch(dueTime: ObserverRenewal);
        await source.Watch(this.AsReference<INeuronObserver>());
    }

    // The source keeps observers for a lease; renewing on a timer also keeps this activation alive.
    private INeuron RenewTriggerWatch(TimeSpan dueTime)
    {
        var source = GrainFactory.GetGrain<INeuron>(GrainId.Parse(Snapshot.Trigger!.Neuron));
        var observer = this.AsReference<INeuronObserver>();
        _triggerSource = source;
        _triggerRenewal?.Dispose();
        _triggerRenewal = this.RegisterGrainTimer(_ => source.Watch(observer),
            new GrainTimerCreationOptions { DueTime = dueTime, Period = ObserverRenewal, Interleave = true, KeepAlive = true });
        return source;
    }

    private async Task UnwatchTriggerAsync()
    {
        _triggerRenewal?.Dispose();
        _triggerRenewal = null;
        if (_triggerSource is not { } source) { return; }
        _triggerSource = null;
        await source.Unwatch(this.AsReference<INeuronObserver>());
    }

    // A token speaks for the current run while the file should run; armed, also for overlapping
    // earlier triggered runs. Stop and Delete revoke every token the file issued.
    public Task<RunAuthorization> Authorize(string runId)
        => Task.FromResult(new RunAuthorization(
            runId.Length > 0 && Snapshot.ShouldRun && (runId == Snapshot.RunId || Snapshot.Trigger is not null), Snapshot.OwnerContext));

    private Task StopCurrentRunAsync(CancellationToken cancellationToken)
        => Snapshot.RunId.Length > 0 ? runner.StopAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken) : Task.CompletedTask;

    private async Task StopReconcilingAsync()
    {
        if (await reminders.GetReminder(this.GetGrainId(), ReconcileReminder) is { } reminder)
        {
            await reminders.UnregisterReminder(this.GetGrainId(), reminder);
        }
    }

    private void RequireSource()
    {
        if (string.IsNullOrWhiteSpace(Snapshot.Source)) { throw new InvalidOperationException("Write the C# source before starting it."); }
    }

    private static (string Principal, CallerContext? Context) Caller()
        => CallerContextStamper.TryGet(out var caller) ? (caller.PrincipalId, caller) : ("system", null);

    // An always-on file that crashed or lost its run is between runs: the reconcile restarts it.
    private CSharpFileSnapshot Describe(CSharpRunState run)
        => new(FileId, Snapshot.Source, Snapshot.Settings,
            Snapshot is { ShouldRun: true, Trigger: null } && run is not { Status: CSharpFileStatus.Running } and not { Status: CSharpFileStatus.Exited, ExitCode: 0 }
                ? CSharpFileStatus.Restarting : run.Status,
            run.ExitCode, run.StartedAt, Snapshot.ShouldRun, Snapshot.Failures, Snapshot.Trigger);

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex SettingName();
}
