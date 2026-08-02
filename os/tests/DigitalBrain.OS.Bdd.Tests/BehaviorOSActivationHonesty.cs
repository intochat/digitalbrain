using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Compositions;
using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class BehaviorOSActivationHonesty
{
    private static readonly Type[] ForbiddenBehaviorDispatchNames =
        new[]
        {
            typeof(OpenHome).Assembly,
            typeof(IDigitalBrain).Assembly,
            typeof(IShell).Assembly,
        }
        .SelectMany(static assembly => assembly.GetExportedTypes())
        .Where(static type =>
            type.Name is "IBehaviorTest" or "BehaviorRunner"
            || type.Name.Contains("BehaviorDispatch", StringComparison.Ordinal))
        .ToArray();

    [Fact(DisplayName =
        "no Behavior-by-name dispatch API — IBehavior marker is synapse-activated, not Run(name)")]
    public void NoBehaviorByNameDispatchApi()
    {
        Assert.Empty(ForbiddenBehaviorDispatchNames);
        Assert.True(typeof(IBehavior).IsInterface);
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(IBehavior)));
    }
}
