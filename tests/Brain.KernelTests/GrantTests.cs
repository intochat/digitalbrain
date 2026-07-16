using Brain.Contracts;
using Xunit;

namespace Brain.KernelTests;

public class GrantTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Foreign_caller_without_grant_fails_closed()
    {
        var neuron = fixture.Neuron("test", Guid.NewGuid().ToString("N"));
        var invocation = new NeuronInvocation("test.echo.v1", "{}", "cmd-1", "other-owner|actor/x|session/1");
        var exception = await Assert.ThrowsAsync<BrainException>(() => neuron.InvokeAsync(invocation));
        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
        Assert.Empty((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Behavior_identity_requires_contract_grant()
    {
        var neuron = fixture.Neuron("test", Guid.NewGuid().ToString("N"));
        var behaviorCaller = "owner|behavior/abc123|behavior/abc123";
        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new("test.echo.v1", "{}", "cmd-1", behaviorCaller)));
        Assert.Equal(BrainErrors.GrantMissing, denied.Code);
    }

    [Fact]
    public async Task Malformed_caller_key_fails_closed_with_stable_code()
    {
        var neuron = fixture.Neuron("test", Guid.NewGuid().ToString("N"));
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new("test.echo.v1", "{}", "cmd-1", "not-a-key")));
        Assert.Equal(BrainErrors.CallerMalformed, exception.Code);
        Assert.Empty((await neuron.ReadEventsAsync(0, 10)).Events);
    }
}
