using System.Reflection;
using System.Text.Json;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Tests.Architecture;

public sealed class OwnerIdentityArchitectureTests
{
    [Fact]
    public void Owner_contracts_round_trip_through_the_Orleans_serializer()
    {
        using var payload = JsonDocument.Parse("{\"messageId\":\"m-1\"}");
        var expected = new CapabilityRequest(
            new("owner-1"),
            new("actor-1"),
            new("installation-1"),
            new(new string('d', 64)),
            "input-1",
            "logical-1",
            "google.gmail.message.read.v1",
            1,
            new ProviderConnectionId("gmail-primary"),
            new GrantRevision(7),
            payload.RootElement,
            DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
            "correlation-1",
            "causation-1");
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(CapabilityRequest).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer<CapabilityRequest>>();

        var actual = serializer.Deserialize(serializer.SerializeToArray(expected));

        Assert.Equal(expected.OwnerId, actual.OwnerId);
        Assert.Equal(expected.ActorId, actual.ActorId);
        Assert.Equal(expected.InstallationId, actual.InstallationId);
        Assert.Equal(expected.ReleaseDigest, actual.ReleaseDigest);
        Assert.Equal(expected.InputId, actual.InputId);
        Assert.Equal(expected.LogicalOperationKey, actual.LogicalOperationKey);
        Assert.Equal(expected.CapabilityId, actual.CapabilityId);
        Assert.Equal(expected.CapabilityVersion, actual.CapabilityVersion);
        Assert.Equal(expected.ProviderConnectionId, actual.ProviderConnectionId);
        Assert.Equal(expected.GrantRevision, actual.GrantRevision);
        Assert.Equal("m-1", actual.Payload.GetProperty("messageId").GetString());
        Assert.Equal(expected.Deadline, actual.Deadline);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(expected.CausationId, actual.CausationId);
    }

    [Fact]
    public void Legacy_partition_identifiers_are_absent_from_internal_contracts()
    {
        var forbidden = new[]
        {
            string.Concat("Tenant", "Id"),
            string.Concat("Workspace", "Id"),
            string.Concat("Workspace", "Ids"),
            string.Concat("Principal", "Ref")
        };
        var assemblies = new[]
        {
            typeof(CapabilityRequest).Assembly,
            typeof(DigitalBrain.Core.Runtime.RequestContext).Assembly,
            typeof(SemanticIntentRequest).Assembly,
            typeof(UiSurfaceSamples).Assembly,
            Assembly.Load("DigitalBrain.Mcp")
        }.Distinct();
        var violations = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .SelectMany(type =>
                new[] { type.Name }
                    .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(static property => property.Name))
                    .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(static field => field.Name))
                    .Select(member => $"{type.FullName}.{member}"))
            .Where(member => forbidden.Any(name => member.EndsWith($".{name}", StringComparison.Ordinal)))
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Flutter_runtime_DTOs_expose_only_owner_and_actor_identity()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            "app/lib/grpc/ui.pb.dart",
            "app/lib/grpc/ui.pbenum.dart",
            "app/lib/runtime/session_state.dart",
            "app/lib/runtime/feed_state.dart",
            "app/lib/runtime/grpc_ui_transport.dart",
            "app/lib/runtime/protocol/surface_protocol.dart"
        };
        var forbidden = new[]
        {
            string.Concat("tenant", "Id"),
            string.Concat("workspace", "Id"),
            string.Concat("principal", "Id"),
            string.Concat("FeedAudience.", "principal"),
            string.Concat("FeedAudience.", "workspace")
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            foreach (var identifier in forbidden)
                Assert.DoesNotContain(identifier, source, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
