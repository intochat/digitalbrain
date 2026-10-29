namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class NeedsBindingFacts
{
    [Fact]
    public void Unbound_sandbox_is_an_issue_naming_module_kind_and_topology()
    {
        var topology = new FakeTopology(Topology.Local, FakeTopology.AllBound with { Sandbox = SandboxBinding.None });

        var issue = Assert.Single(TopologyHosting.ValidateNeeds(topology, [Manifests.Needing("google.drive", Need.Sandbox)]));

        Assert.Contains("google.drive", issue.ToString(), StringComparison.Ordinal);
        Assert.Contains("Sandbox", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Topology=local", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bound_sandbox_is_no_issue()
    {
        Assert.Empty(TopologyHosting.ValidateNeeds(new FakeTopology(Topology.Local), [Manifests.Needing("google.drive", Need.Sandbox)]));
    }

    [Fact]
    public void Named_grain_store_is_bound_by_grain_stores()
    {
        var need = Need.State("drive");
        Assert.True(FakeTopology.AllBound.Binds(need.Kind));
        Assert.False((FakeTopology.AllBound with { GrainStores = GrainStoreBinding.None }).Binds(need.Kind));

        var issue = Assert.Single(TopologyValidator.ValidateNeeds(Topology.Cloud, Bindings.Unbound, [Manifests.Needing("m", need)]));
        Assert.Contains("State(drive)", issue.Message, StringComparison.Ordinal);
    }

    public static readonly TheoryData<NeedKind> Kinds = new(Enum.GetValues<NeedKind>());

    // Every need kind maps to exactly one binding field: a new kind without a mapping fails here.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_need_kind_maps_to_exactly_one_binding(NeedKind kind)
    {
        Assert.True(FakeTopology.AllBound.Binds(kind));
        Assert.False(Bindings.Unbound.Binds(kind));

        var unbinding = new Func<Bindings, Bindings>[]
        {
            b => b with { GrainStores = GrainStoreBinding.None },
            b => b with { Streams = StreamsBinding.None },
            b => b with { Reminders = RemindersBinding.None },
            b => b with { Secrets = SecretsBinding.None },
            b => b with { Http = HttpBinding.None },
            b => b with { Sandbox = SandboxBinding.None },
            b => b with { Model = ModelBinding.None },
            b => b with { Clustering = ClusteringBinding.None },
            b => b with { Ui = UiBinding.None },
        };
        Assert.Equal(1, unbinding.Count(unbind => !unbind(FakeTopology.AllBound).Binds(kind)));
    }

    [Fact]
    public void Every_unbound_need_of_every_module_is_reported()
    {
        var issues = TopologyValidator.ValidateNeeds(Topology.Local, Bindings.Unbound,
            [Manifests.Needing("a", Need.Http, Need.Model), Manifests.Needing("b", Need.Streams)]);

        Assert.Equal(3, issues.Count);
    }
}
