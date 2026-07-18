using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Domains.Tests;

public class CapabilityEnforcerTests
{
    private sealed record FakeSynapse : ISynapse;
    private sealed class FakeHandler : INeuron<FakeSynapse>
    {
        public Task<NeuronResult> HandleAsync(FakeSynapse synapse, NeuronContext ctx, CancellationToken ct)
            => Task.FromResult(NeuronResult.Ok());
    }

    private readonly DomainId _alpha = DomainId.From("alpha");
    private readonly DomainId _gamma = DomainId.From("gamma");

    private CanonicalTarget TargetRequiring(params Capability[] caps) =>
        new(typeof(FakeSynapse), typeof(FakeHandler), _gamma, caps);

    [Fact]
    public void Ambient_caller_bypasses_enforcement()
    {
        var enforcer = new CapabilityEnforcer(new Dictionary<DomainId, IReadOnlyList<Capability>>());

        var act = () => enforcer.AssertCanFire(
            new Caller.Ambient(DomainId.From("domains")),
            TargetRequiring(new Capability.Llm(LlmTier.Reasoning)));

        var ex = Record.Exception(act);
        Assert.Null(ex);
    }

    [Fact]
    public void Exact_match_passes()
    {
        var declarations = new Dictionary<DomainId, IReadOnlyList<Capability>>
        {
            [_alpha] = [new Capability.Llm(LlmTier.Reasoning)],
        };
        var enforcer = new CapabilityEnforcer(declarations);

        var act = () => enforcer.AssertCanFire(
            new Caller.FromDomain(_alpha),
            TargetRequiring(new Capability.Llm(LlmTier.Reasoning)));

        var ex = Record.Exception(act);
        Assert.Null(ex);
    }

    [Fact]
    public void Mismatch_throws_CapabilityDeniedException()
    {
        var declarations = new Dictionary<DomainId, IReadOnlyList<Capability>>
        {
            [_alpha] = [new Capability.Llm(LlmTier.Balanced)],
        };
        var enforcer = new CapabilityEnforcer(declarations);

        var act = () => enforcer.AssertCanFire(
            new Caller.FromDomain(_alpha),
            TargetRequiring(new Capability.Llm(LlmTier.Reasoning)));

        var ex = Assert.Throws<CapabilityDeniedException>(act);
        Assert.Contains("does not declare required capabilities", ex.Message);
    }

    [Fact]
    public void Unregistered_domain_throws()
    {
        var enforcer = new CapabilityEnforcer(new Dictionary<DomainId, IReadOnlyList<Capability>>());

        var act = () => enforcer.AssertCanFire(
            new Caller.FromDomain(DomainId.From("unknown")),
            TargetRequiring());

        var ex = Assert.Throws<CapabilityDeniedException>(act);
        Assert.Contains("not registered", ex.Message);
    }
}
