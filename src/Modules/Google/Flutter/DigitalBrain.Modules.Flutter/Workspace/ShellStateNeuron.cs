using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

[GrainType("intochat.shell-state")]
internal sealed class ShellStateNeuron([PersistentState("head", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ShellHeadState> state) : Grain, IShellState
{
    private IShellPart Part(string hash) => GrainFactory.GetGrain<IShellPart>(this.GetPrimaryKeyString() + "/" + hash);
    public async Task<ShellRead> Read() => new(state.State.Revision, state.State.Root is { } root
        ? (await ShellSnapshotParts.Read(root, hash => Part(hash).Read())).ToJsonString() : null);

    public async Task<ShellRead> Save(long expectedRevision, string operationId, string json)
    {
        if (!Guid.TryParse(operationId, out _)) { throw new ArgumentException("Use a unique operation ID."); }
        var digest = ShellSnapshotParts.Digest(json);
        if (state.State.LastOperation == operationId)
        {
            if (state.State.LastDigest != digest) { throw new InvalidOperationException("Operation ID was reused with different data."); }
            return await Read();
        }
        if (state.State.Revision != expectedRevision) { throw new InvalidOperationException("Workspace changed on another client. Reload before saving."); }
        var snapshot = JsonNode.Parse(json) ?? throw new ArgumentException("Missing snapshot.");
        ShellSnapshotParts.Validate(snapshot);
        var root = await ShellSnapshotParts.Write(snapshot, (hash, value) => Part(hash).Put(value));
        var old = state.State;
        state.State = new()
        {
            Root = root,
            Revision = old.Revision + 1,
            LastOperation = operationId,
            LastDigest = digest
        };
        try { await state.WriteStateAsync(); } catch { state.State = old; throw; }
        return await Read();
    }
}
