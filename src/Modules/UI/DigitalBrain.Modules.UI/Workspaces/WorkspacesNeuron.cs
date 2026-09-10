using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.WorkspacesType)]
internal sealed class WorkspacesNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<WorkspaceIndexState>> state)
    : Neuron<WorkspaceIndexState>(runtime, state), IWorkspaces
{
    public Task<Accepted<WorkspaceRecord>> Ensure(EnsureWorkspace command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("ensure"), command, UIJson.Default.EnsureWorkspace, UIJson.Default.AcceptedWorkspaceRecord, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            var name = arguments.Name.Trim();
            // A name-derived correlation lets the command compute its receipt without reading state;
            // the kernel keeps that receipt (including its timestamp) stable on a command-id retry.
            var correlation = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..32];
            var receipt = new WorkspaceRecord(name, correlation, arguments.Title.Trim(), TimeProvider.GetUtcNow());
            var work = Schedule(UIBodies.Signal(UIVocabulary.WorkspaceEnsuring, receipt, UIJson.Default.WorkspaceRecord));
            return new Accepted<WorkspaceRecord>(receipt, work);
        });

    [ReadOnly]
    public Task<WorkspaceRecord?> Find(FindWorkspace query)
        => Task.FromResult(State?.Workspaces.FirstOrDefault(item =>
            query.Name is { } name ? item.Name == name
                : query.CorrelationId is { } correlation && string.Equals(item.CorrelationId, correlation, StringComparison.OrdinalIgnoreCase)));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != UIVocabulary.WorkspaceEnsuring)
        {
            return;
        }

        var record = UIBodies.Read(delivery, UIJson.Default.WorkspaceRecord);
        var current = State?.Workspaces ?? [];
        await SaveAsync(new WorkspaceIndexState(current.Any(item => item.Name == record.Name)
            ? [.. current.Select(item => item.Name == record.Name ? record : item)]
            : [.. current, record]), cancellationToken).ConfigureAwait(true);
    }
}
