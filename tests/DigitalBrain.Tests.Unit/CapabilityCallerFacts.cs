using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit;

public sealed class CapabilityCallerFacts
{
    [Fact]
    public void AssistantIsAnAgentCaller()
    {
#pragma warning disable CS0618
        var legacy = CapabilityCallers.Of("p", "a", "b", CallerKind.Assistant);
#pragma warning restore CS0618
        Assert.Equal(CallerKind.Agent, legacy.Kind);
        Assert.Equal("assistant", legacy.AppId);
        Assert.Null(legacy.AppKey);
        Assert.Equal(CapabilityCallers.Of("p", "a", "b", CallerKind.Agent).StampedBy, legacy.StampedBy);
    }

    [Fact]
    public void AgentWithoutAppIdKeepsTheLegacyAppId()
    {
        var agent = CapabilityCallers.Of("p", "a", "b", CallerKind.Agent);
        Assert.Equal("assistant", agent.AppId);
        Assert.Equal(CallerKind.Agent, agent.Kind);
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("scheduler")]
    [InlineData("assistant")]
    public void AppRefusesReservedKeys(string key)
    {
        Assert.Throws<ArgumentException>(() => CapabilityCaller.App(key));
    }

    [Fact]
    public void AppCallerCarriesItsKey()
    {
        var app = CapabilityCaller.App("photocraft");
        Assert.Equal("photocraft", app.AppKey);
        Assert.Equal(CallerKind.App, app.Kind);
    }

    [Fact]
    public void CallerKindOrdinalsAreStable()
    {
        Assert.Equal([0, 1, 2, 3, 4], new[] { CallerKind.User, CallerKind.Agent, CallerKind.App, CallerKind.Scheduler, CallerKind.Platform }.Select(kind => (int)kind));
#pragma warning disable CS0618
        Assert.Equal((int)CallerKind.Agent, (int)CallerKind.Assistant);
#pragma warning restore CS0618
    }
}
