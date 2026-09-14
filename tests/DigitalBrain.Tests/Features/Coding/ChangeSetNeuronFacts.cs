using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class ChangeSetNeuronFacts
{
    private static readonly EditRequest FriendlyGreet = new(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""");
    private static readonly EditRequest BrokenGreet = new(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(string name) => 42;");

    private static async Task<(BrainSimulation Brain, DiskFixture Fixture)> StartAsync()
    {
        var fixture = DiskFixture.Create();
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(fixture.Open)),
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, fixture);
    }

    private static IChangeSet ChangeSet(BrainSimulation brain, string id)
        => brain.Grains.GetGrain<IChangeSet>(new NeuronId(CodingVocabulary.ChangeSetType, id).ToGrainId());

    private static async Task<ChangeSetSnapshot> WaitAsync(IChangeSet changeSet, Func<ChangeSetSnapshot, bool> done)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var snapshot = await changeSet.Read();
            if (done(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task Propose_then_check_yields_a_checked_snapshot_with_a_diff()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c1");
        var accepted = await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        Assert.Equal("c1", accepted.Receipt.ChangeId);
        Assert.Equal(1, accepted.Receipt.EditCount);
        await changeSet.Check(new CheckChangeSet(CommandId.New()));
        var checkedSnapshot = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Checked);
        Assert.Contains("+    public string Greet(string name)", checkedSnapshot.Diff, StringComparison.Ordinal);
        Assert.Empty(checkedSnapshot.Diagnostics);
        Assert.Null(checkedSnapshot.Detail);
    }

    [Fact]
    public async Task A_check_with_errors_stays_a_draft_and_names_the_edit()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c2");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), BrokenGreet));
        await changeSet.Check(new CheckChangeSet(CommandId.New()));
        var snapshot = await WaitAsync(changeSet, snapshot => snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Draft, snapshot.Status);
        Assert.StartsWith("edit 1 (ReplaceMember", snapshot.Detail, StringComparison.Ordinal);
        Assert.Contains(snapshot.Diagnostics, hit => hit.Id == "CS0029");
    }

    [Fact]
    public async Task Commit_writes_the_file_and_records_the_generation()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c3");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await changeSet.Commit(new CommitChangeSet(CommandId.New(), "friendlier greeting"));
        var committed = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Committed || snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Committed, committed.Status);
        Assert.Equal([fixture.GreeterPath], committed.Files);
        Assert.Equal(1, committed.Generation);
        Assert.Contains("Hi, {name}", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet)));
        Assert.Contains("committed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Commit_refuses_errors_and_leaves_the_file_alone()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c4");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), BrokenGreet));
        await changeSet.Commit(new CommitChangeSet(CommandId.New(), "broken"));
        var refused = await WaitAsync(changeSet, snapshot => snapshot.Detail is not null);
        Assert.Equal(ChangeSetStatus.Draft, refused.Status);
        Assert.Equal(FixtureSolutions.GreeterSource, await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Discard_closes_the_change_set()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c5");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await changeSet.Discard(new DiscardChangeSet(CommandId.New()));
        var discarded = await WaitAsync(changeSet, snapshot => snapshot.Status == ChangeSetStatus.Discarded);
        Assert.Single(discarded.Edits);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Check(new CheckChangeSet(CommandId.New())));
        Assert.Contains("discarded", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_without_edits_is_refused_with_advice()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ChangeSet(brain, "c6").Check(new CheckChangeSet(CommandId.New())));
        Assert.Contains("Propose at least one edit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_expected_version_is_refused()
    {
        var (brain, fixture) = await StartAsync();
        await using var _ = brain;
        using var __ = fixture;
        var changeSet = ChangeSet(brain, "c7");
        await changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet));
        await WaitAsync(changeSet, snapshot => snapshot.Edits.Count == 1);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changeSet.Propose(new ProposeEdit(CommandId.New(), FriendlyGreet, ExpectedVersion: 0)));
        Assert.Contains("expected version 0", error.Message, StringComparison.Ordinal);
    }
}
