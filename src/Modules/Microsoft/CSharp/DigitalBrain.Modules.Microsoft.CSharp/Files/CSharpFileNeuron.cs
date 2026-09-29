using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Client;
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
    internal const int MaximumPending = 64;
    internal const string ReconcileReminder = "reconcile";
    // Orleans reminders fire at most once a minute.
    internal static readonly TimeSpan ReconcilePeriod = TimeSpan.FromMinutes(1);
    private const int MaximumSettings = 64;
    private readonly Dictionary<string, INeuron> _watched = new(StringComparer.Ordinal);
    private IGrainTimer? _watchRenewal;
    private string FileId => this.GetPrimaryKeyString();
    private bool IsArmed => Snapshot is { ShouldRun: true, Trigger: not null };

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        // A source's own signal may be what activated this file, so watching it now would deadlock;
        // the renewal timer's first tick watches it right after activation instead.
        if (IsArmed || Snapshot.Subscriptions.Length > 0) { RenewSourceWatches(dueTime: TimeSpan.Zero); }
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
        await UnwatchSourcesAsync();
        await StopCurrentRunAsync(cancellationToken);
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), ReconcileReminder, ReconcilePeriod, ReconcilePeriod);
        var owner = Caller();
        // A fresh start is a fresh intent: the new script re-registers its subscriptions by running.
        var runId = await StartRunAsync(Snapshot with { ShouldRun = true, Failures = 0, Owner = owner.Principal, OwnerContext = owner.Context, Trigger = null, Subscriptions = [] }, trigger: null, cancellationToken);
        return Describe(await runner.InspectAsync(owner.Principal, runId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Arm(CSharpTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        RequireSource();
        if (!GrainId.TryParse(trigger.Neuron, out _)) { throw new ArgumentException($"'{trigger.Neuron}' is not a neuron id such as time.timer/tea.", nameof(trigger)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger.Signal);
        await UnwatchSourcesAsync();
        await StopCurrentRunAsync(cancellationToken);
        var owner = Caller();
        await Save(Snapshot with { ShouldRun = true, Failures = 0, Owner = owner.Principal, OwnerContext = owner.Context, Trigger = trigger, Subscriptions = [] }, new CSharpFileChanged(FileId));
        await WatchSourcesAsync();
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), ReconcileReminder, ReconcilePeriod, ReconcilePeriod);
        return Describe(await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        await UnwatchSourcesAsync();
        await StopCurrentRunAsync(cancellationToken);
        var run = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, cancellationToken);
        await Save(Snapshot with { ShouldRun = false, Subscriptions = [] }, new CSharpFileChanged(FileId));
        return Describe(run);
    }

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => runner.LogsAsync(Snapshot.Owner, Snapshot.RunId, tail, cancellationToken);

    public async Task Delete(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        await UnwatchSourcesAsync();
        await StopCurrentRunAsync(cancellationToken);
        await Save(new CSharpFileState(), new CSharpFileChanged(FileId));
        DeactivateOnIdle();
    }

    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        if (IsArmed && signal.GetType().Name == Snapshot.Trigger!.Signal && signal.Publisher == Snapshot.Trigger.Neuron)
        { return this.AsReference<ICSharpFileTrigger>().Fire(signal); }
        return Snapshot.Subscriptions.Any(s => s.Signal == signal.GetType().Name && s.Neuron == signal.Publisher)
            ? this.AsReference<ICSharpFileTrigger>().Enqueue(signal)
            : Task.CompletedTask;
    }

    // Buffers the signal on the subscription it matches, then wakes a run to drain it.
    public async Task Enqueue(Signal signal)
    {
        var json = JsonSerializer.Serialize(signal, signal.GetType(), ScriptEdgeProtocol.Json);
        var name = signal.GetType().Name;
        var changed = false;
        var next = Snapshot.Subscriptions.Select(s =>
        {
            if (s.Signal != name || s.Neuron != signal.Publisher) { return s; }
            changed = true;
            var pending = s.Pending.Length >= MaximumPending ? s.Pending.Skip(1).Append(json) : s.Pending.Append(json);
            return s with { Pending = pending.ToArray() };
        }).ToArray();
        if (!changed) { return; }
        await Save(Snapshot with { Subscriptions = next }, new CSharpPendingArrived(FileId));
        await WakeAsync();
    }

    // Wake-runs are not retried: the next signal is the next attempt, so a file whose wake-runs
    // keep failing disarms instead of burning a run on every signal.
    private async Task WakeAsync()
    {
        if (!Snapshot.ShouldRun) { return; }
        var current = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, CancellationToken.None);
        if (current.Status == CSharpFileStatus.Running) { return; }
        var failures = current switch
        {
            { Status: CSharpFileStatus.Exited, ExitCode: not 0 } => Snapshot.Failures + 1,
            { Status: CSharpFileStatus.Exited } => 0,
            _ => Snapshot.Failures,
        };
        if (failures >= MaximumFailures)
        {
            await StopReconcilingAsync();
            await UnwatchSourcesAsync();
            await Save(Snapshot with { ShouldRun = false, Failures = failures, Subscriptions = [] }, new CSharpFileChanged(FileId));
            return;
        }
        await StartRunAsync(Snapshot with { Failures = failures }, trigger: null, CancellationToken.None);
    }

    public async Task Subscribed(string runId, string neuron, string signal)
    {
        await RequireSpeakingRun(runId);
        if (!GrainId.TryParse(neuron, out _)) { throw new ArgumentException($"'{neuron}' is not a neuron id such as time.timer/tea.", nameof(neuron)); }
        // Watching itself would deadlock this non-reentrant grain and self-feed on every save.
        if (neuron == this.GetGrainId().ToString()) { throw new ArgumentException("A script cannot subscribe to its own file.", nameof(neuron)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        if (!Snapshot.Subscriptions.Any(existing => existing.Neuron == neuron && existing.Signal == signal))
        {
            await Save(Snapshot with { Subscriptions = [.. Snapshot.Subscriptions, new(neuron, signal, [])] }, new CSharpFileChanged(FileId));
        }
        await WatchSourcesAsync();
    }

    public async Task<IReadOnlyList<string>> DrainPending(string runId, string neuron, string signal)
    {
        await RequireSpeakingRun(runId);
        var subscription = Snapshot.Subscriptions.FirstOrDefault(s => s.Neuron == neuron && s.Signal == signal);
        if (subscription is null or { Pending.Length: 0 }) { return []; }
        var drained = subscription.Pending;
        await Save(Snapshot with
        {
            Subscriptions = Snapshot.Subscriptions.Select(s => s == subscription ? s with { Pending = [] } : s).ToArray(),
        }, new CSharpFileChanged(FileId));
        return drained;
    }

    private async Task RequireSpeakingRun(string runId)
    {
        var authorization = await Authorize(runId);
        if (!authorization.Active) { throw new UnauthorizedAccessException("This run no longer speaks for its file."); }
    }

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
            await UnwatchSourcesAsync();
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
            if (_watched.Count == 0) { await WatchSourcesAsync(); }
            return;
        }
        if (Snapshot.Subscriptions.Length > 0)
        {
            // A subscribed file waits between runs; the reminder keeps the watches alive and
            // recovers pending signals a lost run never drained.
            if (_watched.Count == 0) { await WatchSourcesAsync(); }
            var waiting = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, CancellationToken.None);
            if (waiting.Status != CSharpFileStatus.Running && Snapshot.Subscriptions.Any(s => s.Pending.Length > 0))
            { await WakeAsync(); }
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

    private async Task WatchSourcesAsync()
    {
        RenewSourceWatches(dueTime: ObserverRenewal);
        var observer = this.AsReference<INeuronObserver>();
        foreach (var source in _watched.Values) { await source.Watch(observer); }
    }

    // Sources keep observers for a lease; renewing on a timer also keeps this activation alive.
    private void RenewSourceWatches(TimeSpan dueTime)
    {
        _watched.Clear();
        if (Snapshot.Trigger is { } trigger) { _watched[trigger.Neuron] = GrainFactory.GetGrain<INeuron>(GrainId.Parse(trigger.Neuron)); }
        foreach (var subscription in Snapshot.Subscriptions)
        {
            if (!_watched.ContainsKey(subscription.Neuron))
            { _watched[subscription.Neuron] = GrainFactory.GetGrain<INeuron>(GrainId.Parse(subscription.Neuron)); }
        }
        var observer = this.AsReference<INeuronObserver>();
        var sources = _watched.Values.ToArray();
        _watchRenewal?.Dispose();
        _watchRenewal = sources.Length == 0 ? null : this.RegisterGrainTimer(
            async _ => { foreach (var source in sources) { await source.Watch(observer); } },
            new GrainTimerCreationOptions { DueTime = dueTime, Period = ObserverRenewal, Interleave = true, KeepAlive = true });
    }

    private async Task UnwatchSourcesAsync()
    {
        _watchRenewal?.Dispose();
        _watchRenewal = null;
        if (_watched.Count == 0) { return; }
        var observer = this.AsReference<INeuronObserver>();
        var sources = _watched.Values.ToArray();
        _watched.Clear();
        foreach (var source in sources) { await source.Unwatch(observer); }
    }

    // A token speaks for the current run while the file should run; armed or subscribed, any of the
    // file's signed tokens keeps speaking (a wake-run must authorize before its RunId is saved), so
    // for those files only Stop and Delete revoke tokens, not a fresh Start.
    public Task<RunAuthorization> Authorize(string runId)
        => Task.FromResult(new RunAuthorization(
            runId.Length > 0 && Snapshot.ShouldRun
                && (runId == Snapshot.RunId || Snapshot.Trigger is not null || Snapshot.Subscriptions.Length > 0),
            Snapshot.OwnerContext));

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
            Snapshot is { ShouldRun: true, Trigger: null, Subscriptions.Length: 0 } && run is not { Status: CSharpFileStatus.Running } and not { Status: CSharpFileStatus.Exited, ExitCode: 0 }
                ? CSharpFileStatus.Restarting : run.Status,
            run.ExitCode, run.StartedAt, Snapshot.ShouldRun, Snapshot.Failures, Snapshot.Trigger)
        { Subscriptions = Snapshot.Subscriptions.Select(s => new CSharpSubscriptionView(s.Neuron, s.Signal, s.Pending.Length)).ToArray() };

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex SettingName();
}
