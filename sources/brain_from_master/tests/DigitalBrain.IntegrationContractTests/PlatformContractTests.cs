using System.Reflection;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using Orleans;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class PlatformContractTests
{
    [Fact]
    public void Identity_values_are_validated_and_have_stable_serialization_fields()
    {
        Assert.Equal("owner-1", new BrainOwnerId("owner-1").Value);
        Assert.Equal("actor-1", new ActorId("actor-1").Value);
        Assert.Equal("connection-1", new ProviderConnectionId("connection-1").Value);
        Assert.Equal("session-1", new SessionId("session-1").Value);
        Assert.Equal("installation-1", new FeatureInstallationId("installation-1").Value);
        Assert.Equal(new string('a', 64), new ReleaseDigest(new string('A', 64)).Value);
        Assert.Equal(7, new GrantRevision(7).Value);

        Assert.Throws<ArgumentException>(() => new BrainOwnerId(" "));
        Assert.Throws<ArgumentException>(() => new ActorId(new string('a', 257)));
        Assert.Throws<ArgumentException>(() => new ProviderConnectionId("connection\n1"));
        Assert.Throws<ArgumentException>(() => new SessionId(string.Empty));
        Assert.Throws<ArgumentException>(() => new FeatureInstallationId(" "));
        Assert.Throws<ArgumentException>(() => new ReleaseDigest("not-a-digest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantRevision(0));

        foreach (var type in IdentityTypes())
        {
            Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
            Assert.NotNull(type.GetCustomAttribute<AliasAttribute>());
            var value = Assert.Single(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            Assert.Equal(0u, value.GetCustomAttribute<IdAttribute>()?.Id);
        }
    }

    [Fact]
    public void Capability_request_carries_the_complete_bounded_authority_envelope()
    {
        using var payload = JsonDocument.Parse("{\"messageId\":\"m-1\"}");
        var request = new CapabilityRequest(
            new("owner-1"),
            new("actor-1"),
            new("installation-1"),
            new(new string('b', 64)),
            "input-1",
            "summarize-message",
            "google.gmail.message.read",
            1,
            new ProviderConnectionId("gmail-primary"),
            new GrantRevision(7),
            payload.RootElement,
            DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
            "correlation-1",
            "causation-1");

        Assert.Equal("owner-1", request.OwnerId.Value);
        Assert.Equal("actor-1", request.ActorId.Value);
        Assert.Equal("installation-1", request.InstallationId.Value);
        Assert.Equal("input-1", request.InputId);
        Assert.Equal("summarize-message", request.LogicalOperationKey);
        Assert.Equal("google.gmail.message.read", request.CapabilityId);
        Assert.Equal(1, request.CapabilityVersion);
        Assert.Equal("gmail-primary", request.ProviderConnectionId?.Value);
        Assert.Equal(7, request.GrantRevision.Value);
        Assert.Equal("m-1", request.Payload.GetProperty("messageId").GetString());
        Assert.NotNull(typeof(CapabilityRequest).GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.NotNull(typeof(CapabilityRequest).GetCustomAttribute<AliasAttribute>());
        Assert.Equal(14, typeof(CapabilityRequest).GetProperties().Length);
    }

    [Fact]
    public void Capability_request_rejects_oversize_or_incomplete_values()
    {
        using var payload = JsonDocument.Parse("{\"value\":1}");
        var valid = new Func<string, JsonElement, CapabilityRequest>((logicalKey, body) => new(
            new("owner"), new("actor"), new("installation"), new(new string('c', 64)),
            "input", logicalKey, "capability.id", 1, null, new GrantRevision(1), body,
            DateTimeOffset.Parse("2026-07-13T12:00:00Z"), "correlation", "causation"));

        Assert.Throws<ArgumentException>(() => valid(" ", payload.RootElement));
        Assert.Throws<ArgumentException>(() => valid(new string('a', 257), payload.RootElement));
        using var oversized = JsonDocument.Parse($"{{\"value\":\"{new string('x', 65 * 1024)}\"}}");
        Assert.Throws<ArgumentException>(() => valid("logical", oversized.RootElement));
    }

    private static Type[] IdentityTypes() =>
    [
        typeof(BrainOwnerId),
        typeof(ActorId),
        typeof(ProviderConnectionId),
        typeof(SessionId),
        typeof(FeatureInstallationId),
        typeof(ReleaseDigest),
        typeof(GrantRevision)
    ];
}
