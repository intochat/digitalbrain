using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

public sealed class IdentityObservationFacts
{
    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_index_observation_waits_for_the_exact_projected_brain(bool concurrent)
    {
        using var projection = new Projection();
        var checking = Check(new Kit(projection), concurrent);
        await WaitForAnotherRead(checking, projection, TestContext.Current.CancellationToken);
        Assert.False(checking.IsCompleted);
        projection.Show();
        await checking;
    }

    [Theory(Timeout = 30_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unrelated_entry_cannot_satisfy_observation_and_cancellation_is_preserved(bool concurrent)
    {
        using var projection = new Projection(unrelated: true);
        var checking = Check(new Kit(projection), concurrent);
        await WaitForAnotherRead(checking, projection, TestContext.Current.CancellationToken);
        Assert.False(checking.IsCompleted);
        projection.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checking);
    }

    [Fact]
    public async Task Duplicate_projected_brains_still_fail_the_concurrent_provisioning_fact()
    {
        using var projection = new Projection(duplicates: true);
        projection.Show();
        await Assert.ThrowsAsync<Xunit.Sdk.SingleException>(() => new Kit(projection).EnsureBrainIsIdempotentUnderConcurrentCalls());
    }

    private static Task Check(Kit kit, bool concurrent)
        => concurrent ? kit.EnsureBrainIsIdempotentUnderConcurrentCalls() : kit.BrainsOfListsCreatedBrain();

    private static async Task WaitForAnotherRead(Task checking, Projection projection, CancellationToken ct)
    {
        var first = await Task.WhenAny(checking, projection.ReadAgain.Task).WaitAsync(ct);
        if (first == checking) { await checking; }
        Assert.Same(projection.ReadAgain.Task, first);
    }

    private sealed class Kit(Projection projection) : IdentityConformance<IIdentity>
    {
        protected override Task<IdentitySubject> Connect(CancellationToken ct)
        {
            var callers = new AmbientCallerScope();
            var identity = new ProjectedIdentity(callers, projection);
            return Task.FromResult(new IdentitySubject(identity, identity, callers));
        }
    }

    private sealed class ProjectedIdentity(AmbientCallerScope callers, Projection projection) : InMemoryIdentity(callers), IIdentity
    {
        async Task<IReadOnlyList<BrainEntry>> IIdentity.BrainsOf(string principalId, CancellationToken cancellationToken)
            => projection.Read(await base.BrainsOf(principalId, cancellationToken));
    }

    private sealed class Projection(bool unrelated = false, bool duplicates = false) : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new();
        private int _reads;
        private volatile bool _visible;
        public TaskCompletionSource ReadAgain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Show() => _visible = true;
        public void Cancel() => _cancel.Cancel();
        public IReadOnlyList<BrainEntry> Read(IReadOnlyList<BrainEntry> entries)
        {
            _cancel.Token.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _reads) >= 2) { ReadAgain.TrySetResult(); }
            return _visible ? duplicates ? [.. entries, .. entries] : entries
                : unrelated ? [new BrainEntry("unrelated-brain", BrainKind.Personal)] : [];
        }
        public void Dispose() => _cancel.Dispose();
    }
}
