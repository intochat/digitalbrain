using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AiContractBoundaries
{
    [Fact(DisplayName = "ILLM does not inherit IAgent")]
    public void IllmDoesNotInheritIAgent()
    {
        Assert.False(typeof(IAgent).IsAssignableFrom(typeof(ILLM)));
        Assert.DoesNotContain(typeof(IAgent), typeof(ILLM).GetInterfaces());
    }
}
