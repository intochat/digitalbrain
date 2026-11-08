using System.Reflection;
using DigitalBrain.Access;
using DigitalBrain.Apps.Hosting;
using DigitalBrain.Contracts;

namespace DigitalBrain.Tests.Unit.Apps;

public sealed class ProgramsContractFacts
{
    [Fact]
    public void IAppHostAndRecordsHaveExpectedShape()
    {
        var hostMethods = typeof(IAppHost).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Assign", "Describe", "Retire"], hostMethods);

        var b = new BehaviorView("b/apps/x", "h1", "Running", null);
        var pb = new PrebuiltBehavior([1, 2], [3], "h1");
        var a = new ProgramAssignment("b/apps/x", "h0", new Dictionary<string, PrebuiltBehavior> { [""] = pb }, new Dictionary<string, string>());

        Assert.Equal("b/apps/x", a.Owner);
        Assert.Equal("Running", b.Status);
        Assert.Equal("h0", a.Hash);
    }
}
