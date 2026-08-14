using Orleans.Concurrency;

namespace Brain.Abstractions.Graph;

[GenerateSerializer, Immutable]
public sealed record BrainNeuronView
{
    public BrainNeuronView(string id, string moduleId, string roleId, string scope, long firingCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (firingCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firingCount));
        }
        Id = id;
        ModuleId = moduleId;
        RoleId = roleId;
        Scope = scope;
        FiringCount = firingCount;
    }

    [Id(0)] public string Id { get; }
    [Id(1)] public string ModuleId { get; }
    [Id(2)] public string RoleId { get; }
    [Id(3)] public string Scope { get; }
    [Id(4)] public long FiringCount { get; }
}
