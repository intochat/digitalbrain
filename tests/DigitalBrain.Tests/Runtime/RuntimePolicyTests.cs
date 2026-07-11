using DigitalBrain.Core.Runtime;
using System.Text.Json;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class RuntimePolicyTests
{
    [Fact]
    public void Runtime_flags_are_intersected_with_the_profile_manifest()
    {
        var disabledDevelopment = RuntimePolicy.Resolve(
            RuntimeProfile.Development,
            mutationsRequested: false,
            adminRequested: true);
        Assert.True(disabledDevelopment.Allows("brain.read"));
        Assert.False(disabledDevelopment.MutationsEnabled);
        Assert.False(disabledDevelopment.AdminEnabled);
        Assert.False(disabledDevelopment.Allows("brain.act"));
        Assert.False(disabledDevelopment.Allows("brain.admin"));

        var enabledDevelopment = RuntimePolicy.Resolve(
            RuntimeProfile.Development,
            mutationsRequested: true,
            adminRequested: true);
        Assert.True(enabledDevelopment.Allows("brain.act"));
        Assert.True(enabledDevelopment.Allows("brain.approve"));
        Assert.True(enabledDevelopment.Allows("brain.admin"));

        var production = RuntimePolicy.Resolve(
            RuntimeProfile.Production,
            mutationsRequested: true,
            adminRequested: true);
        Assert.True(production.Allows("brain.read"));
        Assert.False(production.MutationsEnabled);
        Assert.False(production.AdminEnabled);
        Assert.False(production.Allows("brain.act"));
        Assert.False(production.Allows("brain.admin"));
    }

    [Fact]
    public async Task Application_boundary_rejects_session_grants_disabled_by_runtime_policy()
    {
        var policy = RuntimePolicy.Resolve(
            RuntimeProfile.Production,
            mutationsRequested: true,
            adminRequested: true);
        var service = new ApplicationService(capabilities: policy.McpCapabilities);
        var context = new RuntimeRequestContext(
            new TenantId("tenant"),
            new WorkspaceId("workspace"),
            new PrincipalRef("user", PrincipalKind.User),
            "session",
            AuthAssurance.Password,
            "correlation",
            "idempotency",
            new HashSet<string>(StringComparer.Ordinal) { "brain.read", "brain.act", "brain.admin" });
        var command = new CommandEnvelope(
            "ino.interact",
            2,
            "command",
            context,
            JsonSerializer.SerializeToElement(new { prompt = "hello" }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitAsync(context, command));
    }
}
