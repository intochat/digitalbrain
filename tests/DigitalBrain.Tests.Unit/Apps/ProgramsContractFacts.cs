using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Apps.Hosting;
using DigitalBrain.Contracts;

namespace DigitalBrain.Tests.Unit.Apps;

public sealed class ProgramsContractFacts
{
    [Fact]
    public void IProgramsHasSixMembersAndExpectedAttributes()
    {
        var type = typeof(IPrograms);
        Assert.Equal("programs", type.GetCustomAttribute<NeuronAliasAttribute>()?.Alias);
        Assert.Equal("digitalbrain.programs", type.GetCustomAttribute<DefaultNeuronTypeAttribute>()?.Type);
        Assert.NotNull(type.GetCustomAttribute<InternalAttribute>());

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(6, methods.Length);

        var interleaves = methods.Where(m => m.GetCustomAttribute<InterleavesAttribute>() is not null)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Assign", "AssignPrebuilt", "Describe", "Settled", "Stage"], interleaves);

        var retire = Assert.Single(methods, m => m.Name == "Retire");
        Assert.Null(retire.GetCustomAttribute<InterleavesAttribute>());
    }

    [Fact]
    public void BehaviorIdsFormatsOwnerAndPath()
    {
        Assert.Equal("b/apps/x", BehaviorIds.For("b/apps/x", ""));
        Assert.Equal("b/apps/x/on-pr.cs", BehaviorIds.For("b/apps/x", "on-pr.cs"));
    }

    [Fact]
    public void IAppHostAndRecordsHaveExpectedShape()
    {
        var hostMethods = typeof(IAppHost).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Assign", "Describe", "Retire"], hostMethods);

        var b = new BehaviorView("b/apps/x", "h1", "Running", null);
        var p = new ProgramView("b/apps/x", "h0", [b]);
        var pb = new PrebuiltBehavior([1, 2], [3], "h1");
        var a = new ProgramAssignment("b/apps/x", "h0", new Dictionary<string, PrebuiltBehavior> { [""] = pb }, new Dictionary<string, string>());

        Assert.Equal("b/apps/x", p.Owner);
        Assert.Single(p.Behaviors);
        Assert.Equal("h0", a.Hash);
    }
}
