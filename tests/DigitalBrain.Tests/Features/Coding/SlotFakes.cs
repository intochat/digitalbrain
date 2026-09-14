using System.Text.Json;
using DigitalBrain.Coding;
using DigitalBrain.Microsoft;

namespace DigitalBrain.Tests.Coding;

internal sealed class FakeSlotBuilder : ISlotBuilder
{
    public List<(string Slot, IReadOnlyList<string> ChangedFiles)> Builds { get; } = [];

    public BuildOutcome Outcome { get; set; } = new(true, [], 0, 1.5, "dotnet build", null);

    public bool TouchesSerializedState { get; set; }

    public string ArtifactsPath { get; set; } = "E:/repo/artifacts/slot-b";

    public Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default)
    {
        Builds.Add((slot, changedFiles));
        return Task.FromResult(new SlotBuildResult(Outcome, ArtifactsPath, TouchesSerializedState));
    }
}

internal sealed class FakeSlotEndpoints : ISlotEndpoints
{
    private int _healthProbes;
    private int _leaseProbes;

    // Answer "not healthy" for this many probes, then healthy: that is a standby that must be started first.
    public int UnhealthyProbes { get; set; }

    // Answer "does not hold the lease yet" for this many probes: that is the standby's refresher lagging.
    public int LeaseProbesBeforeFlip { get; set; }

    public bool NeverSeesTheLease { get; set; }

    // The slot the probed silo says it is. Null means it agrees with whoever asked, which is every
    // correctly configured stand: a different name is the 'DigitalBrain:Slot' typo.
    public string? NamedSlot { get; set; }

    public string? SmokeFailure { get; set; }

    public string? ActiveSlot { get; set; }

    // Each entry answers one switch attempt with the gateway's Retry-After instead of accepting it.
    public Queue<TimeSpan> SwitchRetryAfter { get; } = new();

    public List<string> Switches { get; } = [];

    public List<string> Smokes { get; } = [];

    public List<string> LeaseProbes { get; } = [];

    public Task<bool> HealthyAsync(Uri slotUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(Interlocked.Increment(ref _healthProbes) > UnhealthyProbes);

    public Task<string?> SmokeAsync(Uri slotUrl, string path, CancellationToken cancellationToken = default)
    {
        Smokes.Add(new Uri(slotUrl, path).ToString());
        return Task.FromResult(SmokeFailure);
    }

    public Task<bool> HoldsLeaseAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        LeaseProbes.Add(slot);
        return Task.FromResult(!NeverSeesTheLease && Interlocked.Increment(ref _leaseProbes) > LeaseProbesBeforeFlip);
    }

    public Task<string?> NamedSlotAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(NamedSlot ?? slot);

    public Task<TimeSpan?> SwitchAsync(Uri gatewayUrl, string slot, CancellationToken cancellationToken = default)
    {
        if (SwitchRetryAfter.TryDequeue(out var retryAfter))
        {
            return Task.FromResult<TimeSpan?>(retryAfter);
        }

        Switches.Add(slot);
        ActiveSlot = slot;
        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<string?> ActiveAsync(Uri gatewayUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(ActiveSlot);
}

internal sealed class FakeAspireResourceCommands : IAspireResourceCommands
{
    public List<(string Resource, string Command)> Executed { get; } = [];

    public string? Failure { get; set; }

    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
    {
        Executed.Add((resourceName, command));
        return Failure is { Length: > 0 } failure
            ? Task.FromException<JsonElement>(new InvalidOperationException(failure))
            : Task.FromResult(JsonSerializer.SerializeToElement(new { resourceName, command }));
    }
}
