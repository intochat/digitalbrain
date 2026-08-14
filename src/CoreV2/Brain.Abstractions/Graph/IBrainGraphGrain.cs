namespace Brain.Abstractions.Graph;

public interface IBrainGraphGrain : IGrainWithStringKey
{
    Task<BrainSynapseView> InstallAsync(BrainSynapseChange change);

    Task<BrainSynapseView> ReplaceAsync(Guid synapseId, BrainSynapseChange change);

    Task<BrainSynapseView> RetireAsync(Guid synapseId, string workspaceId, Guid activityId);

    Task RecordUsageAsync(Guid synapseId, string workspaceId, Guid activityId);

    Task<BrainSnapshot> SnapshotAsync(string workspaceId);

    Task<IReadOnlyList<BrainSynapseView>> HistoryAsync(string workspaceId, Guid synapseId);
}
