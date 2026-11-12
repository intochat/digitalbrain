using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

public sealed class SourceBindingsFlipFacts
{
    private static readonly BrainId Brain = NeuronId.Parse("fl/x/y").Brain;

    private static readonly string TwoAliases = SynapseCompilerFacts.Budget + """

        [Signal("flip.charging2")]
        public sealed record Charging2(decimal Amount) : Signal;

        [State("week2")]
        public sealed record WeekSpend2(decimal Spent = 0)
        {
            public WeekSpend2() : this(0) { }
        }

        public static class Budget2
        {
            [Synapse(typeof(Charging2), "week2")]
            public static WeekSpend2 Cap2(Charging2 s, WeekSpend2 t) => t with { Spent = t.Spent + s.Amount };
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Both(string connection) => new()
    {
        ["week"] = new Dictionary<string, string> { ["Db"] = connection },
        ["week2"] = new Dictionary<string, string> { ["Db"] = connection },
    };

    [Fact]
    public async Task AHookThatFlipsMidInstallDoesNotStore()
    {
        var deployment = new FakeDeployment();
        var sources = new FakeSources();
        var loader = new UnitLoader();
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());
        var deployer = new UnitDeployer(compiler, loader, _ => sources, _ => deployment);

        var source = new SynapseSource("Me.Budget", new Dictionary<string, string> { ["Budget.cs"] = TwoAliases }, null);
        var first = await deployer.Deploy(Brain, source with { Bindings = Both("a") }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);
        Assert.Equal(DeployStatus.Deployed, first.Status);
        Assert.Equal(1, deployment.ProposeCalls);

        // A hook flips mid-install, causing the deployment aggregate to refuse the update
        deployment.RefuseNext = "the hook flipped";
        var again = await deployer.Deploy(Brain, source with { Bindings = Both("b") }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), Ct);

        Assert.Equal(DeployStatus.Refused, again.Status);
        Assert.Equal("the hook flipped", again.Reason);
        Assert.Equal(2, deployment.ProposeCalls);
    }

    private sealed class FakeSources : ISources
    {
        public Task<AliasConflict?> Claim(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default) => Task.FromResult<AliasConflict?>(null);
        public Task Release(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> OwnerOf(string alias, CancellationToken ct = default) => Task.FromResult<string?>("Me.Budget");
        public Task<IReadOnlyList<string>> Keys(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["Me.Budget"]);
    }

    private sealed class FakeDeployment : IUnitDeployment
    {
        public int ProposeCalls { get; private set; }
        public string? RefuseNext { get; set; }

        public Task<DeployOutcome> Propose(StoredBuild build, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings, CancellationToken ct = default)
        {
            ProposeCalls++;
            if (RefuseNext is not null)
            {
                return Task.FromResult(new DeployOutcome(DeployStatus.Refused, build.Ref.Hash, [], new Dictionary<string, InstallOutcome>(), RefuseNext));
            }
            return Task.FromResult(new DeployOutcome(DeployStatus.Deployed, build.Ref.Hash, [], new Dictionary<string, InstallOutcome>(), null));
        }

        public Task<BuildRef?> Ref(UnitView view, CancellationToken ct = default) => Task.FromResult<BuildRef?>(null);
        public Task<StoredBuild?> Build(string hash, CancellationToken ct = default) => Task.FromResult<StoredBuild?>(null);
        public Task<UnitSourceView?> Source(CancellationToken ct = default) => Task.FromResult<UnitSourceView?>(null);
        public Task Refuse(string hash, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task Retire(CancellationToken ct = default) => Task.CompletedTask;
    }
}
