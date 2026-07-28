using System.Reflection;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AgentResponseTimeoutContracts
{
    [Fact]
    public void AgentResponsesDeclareProductTimeout()
    {
        var method = typeof(DigitalBrain.AI.IAgent)
            .GetMethod(nameof(DigitalBrain.AI.IAgent.Respond));
        var timeout = method?.GetCustomAttribute<ResponseTimeoutAttribute>();

        Assert.NotNull(timeout);
        Assert.Equal(TimeSpan.FromMinutes(5), timeout.Timeout);
    }
}
