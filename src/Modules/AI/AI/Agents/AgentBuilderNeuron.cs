using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("agent-builder")]
internal sealed class AgentBuilderNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<AgentBuilderState>> state)
    : Neuron<AgentBuilderState>(runtime, state), IAgentBuilder
{
    public Task<IReadOnlyList<AgentBuildSnapshot>> Read()
        => Task.FromResult<IReadOnlyList<AgentBuildSnapshot>>(State?.Builds.Select(static item => item.Snapshot).ToArray() ?? []);

    public async Task<IAgent> Build(BuildAgent command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value == Guid.Empty) { throw new ArgumentException("Build requires a nonempty command id.", nameof(command)); }
        AgentLifecycle.RequireText(command.Key, nameof(command.Key), 128);
        AgentLifecycle.RequireText(command.Instructions, nameof(command.Instructions), 12_000);
        if (command.InitialMessage is not null) { AgentLifecycle.RequireText(command.InitialMessage, nameof(command.InitialMessage), 8_000); }
        if (command.Name?.Length > 128 || command.Owner?.Length > 256) { throw new ArgumentException("Agent name/owner is too long.", nameof(command)); }
        var tools = (command.Tools ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (tools.Length > 32) { throw new ArgumentException("An agent can select at most 32 tools.", nameof(command)); }
        var native = ServiceProvider.GetRequiredService<NativeTools>();
        var invoker = ServiceProvider.GetRequiredService<INeuronInvoker>();
        foreach (var tool in tools)
        {
            if (!native.Contains(tool) && (!NeuronId.TryParse(tool, out var target) || invoker.Describe(target).Count == 0))
            {
                throw new ArgumentException($"Unknown agent tool '{tool}'. Select a registered native tool or a typed neuron address.", nameof(command));
            }
        }

        var hash = AgentLifecycle.Fingerprint(new { command.Key, command.Instructions, command.Model, Tools = tools,
            command.InitialMessage, command.Name, command.Owner, command.Retain });
        var current = State ?? new AgentBuilderState([], []);
        if (current.Closed) { throw new InvalidOperationException("This agent builder is closed because its owner run ended. Use a new builder scope."); }
        if (current.Receipts.FirstOrDefault(receipt => receipt.CommandId == command.Id) is { } receipt && receipt.RequestHash != hash)
        {
            throw new ArgumentException("This build command id already identifies a different request.", nameof(command));
        }
        var build = current.Builds.FirstOrDefault(item => item.Snapshot.Key == command.Key);
        if (build is not null && build.Initialization.RequestHash != hash)
        {
            throw new ArgumentException("This spawn key already identifies an agent with different instructions, model or input.", nameof(command));
        }
        if (build is null)
        {
            if (current.Builds.Count >= 64) { throw new InvalidOperationException("A builder can create at most 64 agents. Use a new builder scope for further work."); }
            var model = ServiceProvider.GetRequiredService<ModelProfiles>().Resolve(command.Model, tools.Length > 0);
            var agentId = new NeuronId("agent", "built-" + AgentLifecycle.Hash(Id + "/" + command.Key));
            var initialTaskId = command.InitialMessage is null ? null : "initial";
            var snapshot = new AgentSnapshot(agentId, command.Name ?? command.Key, "Ready", model,
                command.Instructions, tools, command.Owner, [], TimeProvider.GetUtcNow(), initialTaskId, command.Retain);
            build = new(new(command.Key, agentId, snapshot.Name, "Initializing", command.Retain, snapshot.CreatedAt),
                new(snapshot, hash, command.InitialMessage));
            current = current with { Builds = [.. current.Builds, build] };
        }
        if (!current.Receipts.Any(item => item.CommandId == command.Id))
        {
            if (current.Receipts.Count >= AgentLifecycle.MaxReceipts) { throw new InvalidOperationException("This builder reached its command receipt limit."); }
            current = current with { Receipts = [.. current.Receipts, new(command.Id, hash, null)] };
        }

        // Resolve once and persist the exact model before making the child call. A retry resumes this initialization.
        await SaveAsync(current).ConfigureAwait(true);
        await GrainFactory.GetGrain<IAgentLifecycle>(build.Snapshot.AgentId.ToGrainId()).Initialize(build.Initialization).ConfigureAwait(true);
        build = build with { Snapshot = build.Snapshot with { Status = "Ready" } };
        await SaveAsync(current with { Builds = current.Builds.Select(item => item.Snapshot.Key == command.Key ? build : item).ToArray() }).ConfigureAwait(true);
        return GrainFactory.GetGrain<IAgent>(build.Snapshot.AgentId.ToGrainId());
    }

    public async Task Close(CloseAgentBuilder command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value == Guid.Empty) { throw new ArgumentException("Close requires a nonempty command id.", nameof(command)); }
        var current = State ?? new AgentBuilderState([], []);
        const string hash = "close";
        if (current.Receipts.FirstOrDefault(item => item.CommandId == command.Id) is { } receipt && receipt.RequestHash != hash)
        {
            throw new ArgumentException("This command id already identifies a different builder operation.", nameof(command));
        }
        if (!current.Closed && !current.Receipts.Any(item => item.CommandId == command.Id))
        {
            current = current with { Receipts = [.. current.Receipts, new(command.Id, hash, null)] };
        }
        current = current with { Closed = true };
        await SaveAsync(current).ConfigureAwait(true);
        await RetireChildrenAsync(current).ConfigureAwait(true);
    }

    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        await base.OnNeuronActivatedAsync(cancellationToken).ConfigureAwait(true);
        if (State is { Closed: true } current)
        {
            await RetireChildrenAsync(current).ConfigureAwait(true);
        }
    }

    private async Task RetireChildrenAsync(AgentBuilderState current)
    {
        foreach (var build in current.Builds.Where(static build => !build.Snapshot.Retained))
        {
            var stop = new StopAgent(new CommandId(AgentLifecycle.Signal(Id + "/retire/" + build.Snapshot.Key).Value));
            await GrainFactory.GetGrain<IAgentLifecycle>(build.Snapshot.AgentId.ToGrainId()).Retire(stop).ConfigureAwait(true);
        }
        await SaveAsync(current with { Builds = current.Builds.Select(build => build.Snapshot.Retained ? build
            : build with { Snapshot = build.Snapshot with { Status = "Stopped" } }).ToArray() }).ConfigureAwait(true);
    }

    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GenerateSerializer]
internal sealed record AgentBuildRecord([property: Id(0)] AgentBuildSnapshot Snapshot,
    [property: Id(1)] AgentInitialization Initialization);

[GenerateSerializer]
internal sealed record AgentBuilderState([property: Id(0)] IReadOnlyList<AgentBuildRecord> Builds,
    [property: Id(1)] IReadOnlyList<AgentReceipt> Receipts,
    [property: Id(2)] bool Closed = false);
