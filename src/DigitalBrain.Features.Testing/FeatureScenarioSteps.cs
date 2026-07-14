using Reqnroll;

namespace DigitalBrain.Features.Testing;

[Binding]
public sealed class FeatureScenarioSteps(FeatureScenarioContext scenario, GeneratedFeatureScenario generatedScenario)
{
    [Given("a clean Feature scenario")]
    public void GivenACleanFeatureScenario()
    {
        scenario.Reset();
    }

    [Then("the Feature execution succeeds")]
    public void ThenTheFeatureExecutionSucceeds()
    {
        Require(scenario.LastResult?.Status == FeatureExecutionStatus.Succeeded, $"Expected a successful Feature execution, but was {scenario.LastResult?.Status}.");
    }

    [Then("the Feature execution is identified as a duplicate")]
    public void ThenTheFeatureExecutionIsIdentifiedAsADuplicate()
    {
        Require(scenario.LastResult?.Duplicate == true, "Expected a duplicate Feature execution.");
    }

    [Then("the Feature execution is denied with {string}")]
    public void ThenTheFeatureExecutionIsDeniedWith(string capabilityId)
    {
        Require(
            scenario.LastResult?.Status == FeatureExecutionStatus.Denied &&
            string.Equals(scenario.LastResult.Message, capabilityId, StringComparison.Ordinal),
            $"Expected denial {capabilityId}, but was {scenario.LastResult?.Status}: {scenario.LastResult?.Message}.");
    }

    [Then("the Feature execution fails with {string}")]
    public void ThenTheFeatureExecutionFailsWith(string message)
    {
        Require(
            scenario.LastResult?.Status == FeatureExecutionStatus.Failed &&
            string.Equals(scenario.LastResult.Message, message, StringComparison.Ordinal),
            $"Expected failure {message}, but was {scenario.LastResult?.Status}: {scenario.LastResult?.Message}.");
    }

    [When("the generated Feature input is delivered twice")]
    public Task WhenTheGeneratedFeatureInputIsDeliveredTwice() =>
        generatedScenario.ExecuteTwiceAsync();

    [Then("the generated duplicate gate succeeds")]
    public void ThenTheGeneratedDuplicateGateSucceeds()
    {
        Require(
            generatedScenario.FirstResult?.Status == FeatureExecutionStatus.Succeeded && generatedScenario.FirstResult.Duplicate == false,
            "Expected the first generated delivery to succeed as original work.");
        Require(
            generatedScenario.SecondResult?.Status == FeatureExecutionStatus.Succeeded && generatedScenario.SecondResult.Duplicate,
            "Expected the second generated delivery to succeed as a duplicate.");
        Require(generatedScenario.HandlerExecutionCount == 1, $"Expected one handler execution, but observed {generatedScenario.HandlerExecutionCount}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
