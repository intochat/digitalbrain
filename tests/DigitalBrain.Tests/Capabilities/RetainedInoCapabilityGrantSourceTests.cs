using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Tests.Capabilities;

public sealed class RetainedInoCapabilityGrantSourceTests
{
    private static readonly BrainOwnerId Owner = new("owner-1");
    private static readonly ActorId Actor = new("actor-1");

    [Fact]
    public async Task Live_policy_can_remove_pause_and_restore_a_scoped_grant()
    {
        var configuration = new ConfigurationManager
        {
            [RetainedInoCapabilityGrantSource.EnabledKey] = "false"
        };
        var source = new RetainedInoCapabilityGrantSource(configuration);
        var request = Request();

        Assert.Null(await source.ReadAsync(request));

        configuration[RetainedInoCapabilityGrantSource.EnabledKey] = "true";
        configuration[RetainedInoCapabilityGrantSource.PausedKey] = "true";
        var paused = await source.ReadAsync(request);

        Assert.NotNull(paused);
        Assert.True(paused.Paused);
        Assert.True(paused.AllowsTool(GmailTools.ReadMessages));
        Assert.False(paused.AllowsTool(GmailTools.Send));

        configuration[RetainedInoCapabilityGrantSource.PausedKey] = "false";
        var restored = await source.ReadAsync(request);

        Assert.NotNull(restored);
        Assert.False(restored.Paused);
    }

    [Fact]
    public async Task Policy_does_not_issue_a_grant_for_tampered_authority_coordinates()
    {
        var configuration = new ConfigurationManager
        {
            [RetainedInoCapabilityGrantSource.EnabledKey] = "true"
        };
        var source = new RetainedInoCapabilityGrantSource(configuration);
        var valid = Request();
        var tampered = new CapabilityRequest(
            valid.OwnerId,
            valid.ActorId,
            valid.InstallationId,
            valid.ReleaseDigest,
            valid.InputId,
            valid.LogicalOperationKey,
            valid.CapabilityId,
            valid.CapabilityVersion,
            new ProviderConnectionId("wrong-connection"),
            valid.GrantRevision,
            valid.Payload,
            valid.Deadline,
            valid.CorrelationId,
            valid.CausationId);

        Assert.Null(await source.ReadAsync(tampered));
    }

    private static CapabilityRequest Request() => RetainedInoCapabilityAuthority.CreateRequest(
        Owner,
        Actor,
        "input-1",
        "operation-1",
        GoogleCapabilityIds.GmailMailboxRead,
        JsonSerializer.SerializeToElement(new { }),
        new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
        "correlation-1");
}
