using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Microsoft.CSharp;

// Start records the intent to run; a reminder reconciles it with the sandbox, so a run lost with its
// container comes back and a crashing script is retried a bounded number of times.
[GrainType("microsoft.csharp.file")]
internal sealed partial class CSharpFileNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CSharpFileState> store,
    SandboxCSharpRunner runner,
    IReminderRegistry reminders)
    : Neuron<CSharpFileState>(store), ICSharpFile, IRemindable
{
    internal const int MaximumSourceBytes = 128 * 1024;
    internal const int MaximumFailures = 5;
    internal const string ReconcileReminder = "reconcile";
    // Orleans reminders fire at most once a minute.
    internal static readonly TimeSpan ReconcilePeriod = TimeSpan.FromMinutes(1);
    private const int MaximumSettings = 64;
    private string FileId => this.GetPrimaryKeyString();

    public async Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default)
        => Describe(await runner.InspectAsync(Snapshot.RunId, cancellationToken));

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
        if (string.IsNullOrWhiteSpace(Snapshot.Source)) { throw new InvalidOperationException("Write the C# source before starting it."); }
        if (Snapshot.RunId.Length > 0) { await runner.StopAsync(Snapshot.RunId, cancellationToken); }
        var owner = CallerContextStamper.TryGet(out var caller) ? caller.PrincipalId : "system";
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), ReconcileReminder, ReconcilePeriod, ReconcilePeriod);
        var runId = await StartRunAsync(Snapshot with { ShouldRun = true, Failures = 0, Owner = owner }, cancellationToken);
        return Describe(await runner.InspectAsync(runId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        await runner.StopAsync(Snapshot.RunId, cancellationToken);
        var run = await runner.InspectAsync(Snapshot.RunId, cancellationToken);
        await Save(Snapshot with { ShouldRun = false }, new CSharpFileChanged(FileId));
        return Describe(run);
    }

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => runner.LogsAsync(Snapshot.RunId, tail, cancellationToken);

    public async Task Delete(CancellationToken cancellationToken = default)
    {
        await StopReconcilingAsync();
        if (Snapshot.RunId.Length > 0) { await runner.StopAsync(Snapshot.RunId, cancellationToken); }
        await Save(new CSharpFileState(), new CSharpFileChanged(FileId));
        DeactivateOnIdle();
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != ReconcileReminder) { return; }
        if (!Snapshot.ShouldRun)
        {
            await StopReconcilingAsync();
            return;
        }
        var run = await runner.InspectAsync(Snapshot.RunId, CancellationToken.None);
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
                await StartRunAsync(Snapshot with { Failures = Snapshot.Failures + 1 }, CancellationToken.None);
                return;
            default:
                // The run or its whole sandbox is gone; that is not the script's failure.
                await StartRunAsync(Snapshot, CancellationToken.None);
                return;
        }
    }

    private async Task<string> StartRunAsync(CSharpFileState next, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        await runner.StartAsync(runId, next.Source, next.Settings, cancellationToken);
        await Save(next with { RunId = runId }, new CSharpFileChanged(FileId));
        return runId;
    }

    private async Task StopReconcilingAsync()
    {
        if (await reminders.GetReminder(this.GetGrainId(), ReconcileReminder) is { } reminder)
        {
            await reminders.UnregisterReminder(this.GetGrainId(), reminder);
        }
    }

    // A file that should run but crashed or lost its run is between runs: the reconcile restarts it.
    private CSharpFileSnapshot Describe(CSharpRunState run)
        => new(FileId, Snapshot.Source, Snapshot.Settings,
            Snapshot.ShouldRun && run is not { Status: CSharpFileStatus.Running } and not { Status: CSharpFileStatus.Exited, ExitCode: 0 }
                ? CSharpFileStatus.Restarting : run.Status,
            run.ExitCode, run.StartedAt, Snapshot.ShouldRun, Snapshot.Failures);

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex SettingName();
}
