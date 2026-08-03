using DigitalBrain.Behaviors;
using Reqnroll;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public sealed class BehaviorSteps
{
    [Then("behavior contracts require scenario evidence before approval")]
    public void ThenBehaviorContractsRequireScenarioEvidence()
    {
        var approve = typeof(IBehaviorNeuron).GetMethod(nameof(IBehaviorNeuron.Approve));
        Assert.NotNull(approve);
        Assert.Equal(typeof(BehaviorRevisionApproval), approve!.GetParameters()[0].ParameterType);
    }
}
