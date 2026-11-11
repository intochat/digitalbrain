#pragma warning disable CA2201
#pragma warning disable CA1822
#pragma warning disable CA1040
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Storage;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Signals;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

/// <summary>
/// Empirical adversarial stress test harness for Milestone 3:
/// Atomic Journal Write Protocol (R04) and Concurrency Challenger.
/// Stress-tests race conditions, optimistic concurrency conflicts, commit atomicity,
/// KernelState drop callbacks, and TestOS resource lifecycle.
/// </summary>
public sealed class AtomicJournalChallengerFacts
{
    private static readonly BrainId TestBrain = new("b1");
    private static readonly NeuronId PublisherP = NeuronId.Parse("b1/chat/p");
    private static readonly NeuronId PublisherQ = NeuronId.Parse("b1/chat/q");

    // =========================================================================
    // 1. Concurrency & Stale Versions on MemoryJournal
    // =========================================================================

    [Fact]
    public async Task MemoryJournal_high_concurrency_initial_write_race_allows_exactly_one_winner()
    {
        var journal = new MemoryJournal(TestBrain, new ManualTime());
        const int concurrency = 50;
        var barrier = new Barrier(concurrency);
        var successes = new ConcurrentBag<CommitResult>();
        var staleExceptions = new ConcurrentBag<StaleVersionException>();

        var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            try
            {
                var commit = new Commit(
                    PublisherP,
                    new StateWrite("counter", StateChange.Write, Encoding.UTF8.GetBytes($"{{\"thread\":{i}}}"), Expected: null),
                    [],
                    null,
                    new Stamp("trace-init", "cause-init"));

                var res = await journal.Append(commit, TestContext.Current.CancellationToken);
                successes.Add(res);
            }
            catch (StaleVersionException ex)
            {
                staleExceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Exactly 1 winner
        Assert.Single(successes);
        Assert.Equal(1L, successes.First().Version);

        // Exactly concurrency - 1 losers
        Assert.Equal(concurrency - 1, staleExceptions.Count);
        foreach (var ex in staleExceptions)
        {
            Assert.Equal(PublisherP.ToString(), ex.Owner);
            Assert.Equal("counter", ex.Name);
            Assert.Equal(1L, ex.Stored);
            Assert.Null(ex.Expected);
        }

        // Stored record matches winner version
        var record = await journal.ReadRecord(PublisherP, "counter", TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.Equal(1L, record.Version);
    }

    [Fact]
    public async Task MemoryJournal_high_concurrency_update_race_allows_exactly_one_winner()
    {
        var journal = new MemoryJournal(TestBrain, new ManualTime());
        // Initialize to version 1
        await journal.Append(new Commit(
            PublisherP,
            new StateWrite("counter", StateChange.Write, "init"u8.ToArray(), Expected: null),
            [],
            null,
            new Stamp(null, null)), TestContext.Current.CancellationToken);

        const int concurrency = 50;
        var barrier = new Barrier(concurrency);
        var successes = new ConcurrentBag<CommitResult>();
        var staleExceptions = new ConcurrentBag<StaleVersionException>();

        var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            try
            {
                var commit = new Commit(
                    PublisherP,
                    new StateWrite("counter", StateChange.Write, Encoding.UTF8.GetBytes($"{{\"update\":{i}}}"), Expected: 1L),
                    [],
                    null,
                    new Stamp("trace-update", "cause-update"));

                var res = await journal.Append(commit, TestContext.Current.CancellationToken);
                successes.Add(res);
            }
            catch (StaleVersionException ex)
            {
                staleExceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Exactly 1 winner reached version 2
        Assert.Single(successes);
        Assert.Equal(2L, successes.First().Version);

        // Exactly concurrency - 1 losers
        Assert.Equal(concurrency - 1, staleExceptions.Count);
        foreach (var ex in staleExceptions)
        {
            Assert.Equal(PublisherP.ToString(), ex.Owner);
            Assert.Equal("counter", ex.Name);
            Assert.Equal(2L, ex.Stored);
            Assert.Equal(1L, ex.Expected);
        }

        // Stored record is version 2
        var record = await journal.ReadRecord(PublisherP, "counter", TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.Equal(2L, record.Version);
    }

    // =========================================================================
    // 2. Commit Atomicity: All or Nothing
    // =========================================================================

    [Fact]
    public async Task Commit_atomicity_stale_cursor_rejects_state_and_signals_leaving_zero_residue()
    {
        var journal = new MemoryJournal(TestBrain, new ManualTime());

        // Prepare a commit with valid state, 3 signals, but an invalid cursor (Expected: 999)
        var stateBytes = "valid state"u8.ToArray();
        var cursorBytes = Subscriptions.None(PublisherP).Encode();
        var badCommit = new Commit(
            PublisherP,
            new StateWrite("state", StateChange.Write, stateBytes, Expected: null),
            [new Note("s1"), new Note("s2"), new Note("s3")],
            new CursorMove("cursor", cursorBytes, Expected: 999),
            new Stamp("trace-bad", "cause-bad"));

        var ex = await Assert.ThrowsAsync<StaleVersionException>(() => journal.Append(badCommit, TestContext.Current.CancellationToken));
        Assert.Equal("cursor", ex.Name);

        // Zero residue verification:
        // 1. State must not exist
        var stateRecord = await journal.ReadRecord(PublisherP, "state", TestContext.Current.CancellationToken);
        Assert.Null(stateRecord);

        // 2. Cursor must not exist
        var cursorRecord = await journal.ReadRecord(PublisherP, "cursor", TestContext.Current.CancellationToken);
        Assert.Null(cursorRecord);

        // 3. Signals must not be in view
        Assert.Equal(0L, journal.Last.Value);
        Assert.Equal(0L, journal.LastOf(PublisherP).Value);
        var page = await journal.ReadAfter(new Position(0), PublisherP, 10, TestContext.Current.CancellationToken);
        Assert.Empty(page.Signals);

        // 4. Subscriptions index must not have been folded
        Assert.Null(journal.Subscriptions.Of(PublisherP));

        // 5. Subsequent valid commit gets sequence 1 and position 1 (no consumed sequences)
        var goodCommit = new Commit(
            PublisherP,
            new StateWrite("state", StateChange.Write, stateBytes, Expected: null),
            [new Note("good")],
            new CursorMove("cursor", cursorBytes, Expected: null),
            new Stamp("trace-good", "cause-good"));

        var goodResult = await journal.Append(goodCommit, TestContext.Current.CancellationToken);
        Assert.Equal(1L, goodResult.Version);
        Assert.Equal(1L, goodResult.FirstSequence);
        Assert.Equal(1L, goodResult.FirstPosition.Value);
    }

    [Fact]
    public async Task Commit_atomicity_stale_state_rejects_cursor_and_signals_leaving_zero_residue()
    {
        var journal = new MemoryJournal(TestBrain, new ManualTime());

        // Prepare a commit with stale state (Expected: 50), valid signals, valid cursor
        var cursorBytes = Subscriptions.None(PublisherP).Encode();
        var badCommit = new Commit(
            PublisherP,
            new StateWrite("state", StateChange.Write, "bytes"u8.ToArray(), Expected: 50),
            [new Note("orphan1"), new Note("orphan2")],
            new CursorMove("cursor", cursorBytes, Expected: null),
            new Stamp("trace-bad", "cause-bad"));

        var ex = await Assert.ThrowsAsync<StaleVersionException>(() => journal.Append(badCommit, TestContext.Current.CancellationToken));
        Assert.Equal("state", ex.Name);

        // Verifications
        Assert.Null(await journal.ReadRecord(PublisherP, "state", TestContext.Current.CancellationToken));
        Assert.Null(await journal.ReadRecord(PublisherP, "cursor", TestContext.Current.CancellationToken));
        Assert.Equal(0L, journal.Last.Value);
        Assert.Empty(journal.Subscriptions.All);
    }

    // =========================================================================
    // 3. KernelState Activation Dropping on StaleVersionException
    // =========================================================================

    private sealed class FaultingJournal(BrainId brain) : IBrainJournal
    {
        public BrainId Brain => brain;
        public bool ThrowStale { get; set; } = true;

        public Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        {
            if (ThrowStale)
            {
                throw new StaleVersionException(commit.Publisher.ToString(), "state", 10, commit.State?.Expected);
            }
            return Task.FromResult(new CommitResult(11, 1, new Position(1), DateTimeOffset.UtcNow));
        }

        public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
            => Task.FromResult<JournalRecord?>(new JournalRecord("init"u8.ToArray(), 10, null));

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoOpNudge : ISignalNudge
    {
        public Task Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> appended, Position last, CancellationToken ct = default) => Task.CompletedTask;
        public void Appended(NeuronId publisher, IReadOnlyCollection<string> aliases, Position last) { }
        public Task Offered(NeuronId publisher, Signal signal, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task KernelState_drops_activation_on_Save_Store_Clear_and_Publish()
    {
        var id = PublisherP;
        var journal = new FaultingJournal(id.Brain);
        var nudge = new NoOpNudge();

        // 1. Test Save
        var droppedSave = false;
        var state1 = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => droppedSave = true);
        await Assert.ThrowsAsync<StaleVersionException>(() => state1.Save("test", [new Note("s")]));
        Assert.True(droppedSave);

        // 2. Test Store
        var droppedStore = false;
        var state2 = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => droppedStore = true);
        await Assert.ThrowsAsync<StaleVersionException>(() => state2.Store("test"));
        Assert.True(droppedStore);

        // 3. Test Clear
        var droppedClear = false;
        var state3 = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => droppedClear = true);
        await Assert.ThrowsAsync<StaleVersionException>(() => state3.Clear());
        Assert.True(droppedClear);

        // 4. Test Durable Publish
        var droppedPublish = false;
        var state4 = new KernelState(id, "state", stateless: false, journal, nudge, () => new Stamp(null, null), () => droppedPublish = true);
        await Assert.ThrowsAsync<StaleVersionException>(() => state4.Publish(new Note("durable")));
        Assert.True(droppedPublish);
    }

    // =========================================================================
    // 4. Concurrency & Atomicity on File-Backed BrainLogJournal
    // =========================================================================

    [Fact]
    public async Task BrainLogJournal_file_backed_concurrency_race_ensures_strict_version_ordering()
    {
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);
        var log = await store.Log(TestBrain.Value);
        var history = store.History(TestBrain.Value);
        var journal = new BrainLogJournal(store.Logs, history, id => BrainStore.GrainOf(id).ToString());

        const int concurrency = 20;
        var barrier = new Barrier(concurrency);
        var successes = new ConcurrentBag<CommitResult>();
        var staleExceptions = new ConcurrentBag<StaleVersionException>();

        // Phase 1: Race on initial write (Expected: null)
        var phase1 = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            try
            {
                var commit = new Commit(
                    PublisherP,
                    new StateWrite("state", StateChange.Write, Encoding.UTF8.GetBytes($"{{\"thread\":{i}}}"), Expected: null),
                    [new Note($"init-{i}")],
                    null,
                    new Stamp("trace-p1", "cause-p1"));

                var res = await journal.Append(commit, TestContext.Current.CancellationToken);
                successes.Add(res);
            }
            catch (StaleVersionException ex)
            {
                staleExceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(phase1);

        Assert.Single(successes);
        Assert.Equal(1L, successes.First().Version);
        Assert.Equal(concurrency - 1, staleExceptions.Count);

        // Phase 2: Reopen from disk to prove durable recovery matches in-memory aggregate
        await store.Reopen();
        var reopenedJournal = new BrainLogJournal(store.Logs, store.History(TestBrain.Value), id => BrainStore.GrainOf(id).ToString());
        var record = await reopenedJournal.ReadRecord(PublisherP, "state", TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.Equal(1L, record.Version);

        // Read signals to verify exactly the winner's signal was stored
        var page = await reopenedJournal.Read(PublisherP, 0, 10, TestContext.Current.CancellationToken);
        Assert.Single(page.Signals);
        Assert.Equal(1L, page.Signals[0].Sequence);
    }

    // =========================================================================
    // 5. TestOS Resource Lifecycle & Stale Version Drop
    // =========================================================================

    public interface IChallengeNeuron
    {
        Task SaveState(string text);
        Task<string?> ReadState();
    }

    public sealed record ChallengeState(string Text)
    {
        public ChallengeState() : this(string.Empty) { }
    }

    [Neuron("challenge.test")]
    public sealed class ChallengeNeuron : Neuron<ChallengeState>, IChallengeNeuron
    {
        public Task SaveState(string text) => Store(new ChallengeState(text));
        public Task<string?> ReadState() => Task.FromResult<string?>(State.Text);
    }

    [Fact]
    public async Task TestOS_stale_version_triggers_host_drop_and_reactivation_clears_host()
    {
        await using var os = DigitalBrain.Testing.TestOS.Local();
        os.Register<ChallengeNeuron>();
        var id = NeuronId.Parse("b1/challenge.test/1");

        var proxy = os.Get<IChallengeNeuron>(id);
        await proxy.SaveState("v1");
        await os.Idle();
        Assert.True(os.IsActive(id));
        Assert.Equal("v1", await proxy.ReadState());

        // Concurrent update behind the host's back: advance record to version 2 in journal
        IBrainJournal journal = os.Journal(id.Brain);
        await journal.Append(new Commit(
            id,
            new StateWrite("state", StateChange.Write, "{\"Text\":\"concurrent-update\"}"u8.ToArray(), Expected: 1L),
            [],
            null,
            new Stamp(null, null)), TestContext.Current.CancellationToken);

        // Active host still holds cached _version = 1. Its next write will fail optimistic concurrency!
        var ex = await Assert.ThrowsAsync<StaleVersionException>(() => proxy.SaveState("v2-attempt"));
        Assert.Equal(2L, ex.Stored);
        Assert.Equal(1L, ex.Expected);
        await os.Idle();

        // The activation MUST have been dropped via drop() -> Deactivate(Failed)
        Assert.False(os.IsActive(id));

        // The next invocation reactivates with fresh state loaded from journal
        var current = await proxy.ReadState();
        Assert.Equal("concurrent-update", current);
        Assert.True(os.IsActive(id));
    }

    [Fact]
    public async Task TestOS_restart_deactivates_all_hosts_and_recovers_state()
    {
        await using var os = DigitalBrain.Testing.TestOS.Local();
        os.Register<ChallengeNeuron>();
        var id = NeuronId.Parse("b1/challenge.test/restart-test");

        var proxy = os.Get<IChallengeNeuron>(id);
        await proxy.SaveState("persistent-payload");
        await os.Idle();
        Assert.True(os.IsActive(id));

        // Restart stops all hosts
        await os.Restart();
        await os.Idle();
        Assert.False(os.IsActive(id));

        // Reactivation recovers state correctly
        Assert.Equal("persistent-payload", await proxy.ReadState());
        Assert.True(os.IsActive(id));
    }

    // =========================================================================
    // 6. Collectible ALC Resource Lifecycle & Leak Prevention
    // =========================================================================

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference WeakContext, Task<bool> UnloadTask) LoadUseAndUnload(SynapseLoader loader)
    {
        var sourceText = """
            namespace Me.TestCollectible;
            [Signal("test.collectible")]
            public sealed record CollectibleSignal(string Data) : Signal;
            [State("item")]
            public sealed record ItemState(string Value)
            {
                public ItemState() : this("") { }
            }
            public static class Module
            {
                [Synapse(typeof(CollectibleSignal), "item")]
                public static ItemState OnSignal(CollectibleSignal s, ItemState t) => new(s.Data);
            }
            """;
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());
        var build = compiler.Compile(new SynapseSource("Me.TestCollectible", new Dictionary<string, string> { ["Source.cs"] = sourceText }, null));
        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        var loaded = loader.Load(build);
        var alc = AssemblyLoadContext.GetLoadContext(loaded.Assembly)!;
        var weak = new WeakReference(alc);

        // Verify RuntimeProtocol does not leak options or pin the collectible ALC
        var stateType = loaded.States[0];
        var options = RuntimeProtocol.OptionsFor(stateType);
        Assert.NotNull(options);
        RuntimeProtocol.Forget(alc);

        var unloadTask = loader.Unload(loaded);
        return (weak, unloadTask);
    }

    [Fact]
    public async Task Collectible_synapse_assembly_unloads_cleanly_without_retaining_ALC_references()
    {
        var loader = new SynapseLoader();
        var (weakContext, unloadTask) = LoadUseAndUnload(loader);
        var collected = await unloadTask;
        Assert.True(collected);
        Assert.False(weakContext.IsAlive);
    }
}
