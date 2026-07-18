using System.Reflection;
using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Xunit;

namespace Brain.Tests.Contracts;

public sealed class ContractSurfaceTests
{
    [Fact]
    public void Identity_types_round_trip_values()
    {
        Assert.Equal("org-1", new OrganizationId("org-1").Value);
        Assert.Equal("principal-1", new PrincipalId("principal-1").Value);
        Assert.Equal("space-1", new SpaceId("space-1").Value);
    }

    [Fact]
    public void NeuronAddress_composes_organization_space_contract_and_instance()
    {
        var address = new NeuronAddress(
            new OrganizationId("org-1"),
            new SpaceId("space-1"),
            "chat.group.v1",
            "chat-1");

        Assert.Equal("org-1|space-1|chat.group.v1/chat-1", address.ToGrainKey());
        Assert.Equal(address, NeuronAddress.Parse(address.ToGrainKey()));
    }

    [Fact]
    public void NeuronContractAttribute_is_explicit_on_typed_interfaces()
    {
        Assert.Equal("agent.gpt56.v1", typeof(IGpt56).GetCustomAttribute<NeuronContractAttribute>()!.ContractId);
        Assert.Equal("agent.grok45.v1", typeof(IGrok45).GetCustomAttribute<NeuronContractAttribute>()!.ContractId);
        Assert.Equal("chat.group.v1", typeof(IGroupChat).GetCustomAttribute<NeuronContractAttribute>()!.ContractId);
        Assert.Equal("google.gmail.v1", typeof(IGmail).GetCustomAttribute<NeuronContractAttribute>()!.ContractId);
        Assert.Equal("salesforce.v1", typeof(ISalesforce).GetCustomAttribute<NeuronContractAttribute>()!.ContractId);
    }

    [Fact]
    public void Command_and_event_synapses_carry_typed_payload_and_metadata()
    {
        var metadata = new SynapseMetadata(
            CommandId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EventId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CausationId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CorrelationId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            OrganizationId: new OrganizationId("org-1"),
            PrincipalId: new PrincipalId("principal-1"),
            SpaceId: new SpaceId("space-1"),
            Source: new NeuronAddress(new OrganizationId("org-1"), new SpaceId("space-1"), "chat.group.v1", "chat-1"),
            SourceSequence: 7,
            CausalDepth: 1,
            OccurredAt: DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        var command = new CommandSynapse<StartDiscussion>(
            metadata,
            new StartDiscussion("topic", "org-1|space-1|agent.gpt56.v1/gpt-1", "org-1|space-1|agent.grok45.v1/grok-1"));
        var @event = new EventSynapse<string>(metadata, "payload");

        Assert.Equal("topic", command.Payload.Topic);
        Assert.Equal(7, command.Metadata.SourceSequence);
        Assert.Equal("payload", @event.Payload);
        Assert.Equal(metadata.EventId, @event.Metadata.EventId);
    }

    [Fact]
    public void CommandReceipt_carries_command_identity_and_status()
    {
        var receipt = new CommandReceipt(
            CommandId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status: CommandReceiptStatus.Accepted,
            Revision: 3,
            FailureCode: null,
            FailureMessage: null);

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(3, receipt.Revision);
    }

    [Fact]
    public void Ui_surface_types_support_snapshot_and_patch()
    {
        var action = new UiAction("approve", "Approve", ExpectedRevision: 2);
        var block = new UiBlock("text", "Hello", [action]);
        var surface = new UiSurface("surface-1", Revision: 2, [block]);
        var snapshot = new UiSurfaceSnapshot(surface);
        var patch = new UiSurfacePatch(
            SurfaceId: "surface-1",
            FromRevision: 1,
            ToRevision: 2,
            [new UiPatchOperation("replace", "/blocks/0/text", "Hello")]);

        Assert.Equal(2, snapshot.Surface.Revision);
        Assert.Equal("approve", snapshot.Surface.Blocks[0].Actions[0].Id);
        Assert.Single(patch.Operations);
        Assert.Equal("replace", patch.Operations[0].Op);
    }

    [Fact]
    public void Agent_interfaces_form_typed_hierarchy()
    {
        Assert.True(typeof(IAgent).IsAssignableFrom(typeof(IGpt56)));
        Assert.True(typeof(IAgent).IsAssignableFrom(typeof(IGrok45)));
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(IGroupChat)));
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(IGmail)));
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(ISalesforce)));
    }

    [Fact]
    public void Contracts_serialize_with_Orleans()
    {
        var services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        var deepCopier = services.GetRequiredService<DeepCopier>();

        var metadata = new SynapseMetadata(
            CommandId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            EventId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CausationId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CorrelationId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            OrganizationId: new OrganizationId("org-ser"),
            PrincipalId: new PrincipalId("principal-ser"),
            SpaceId: new SpaceId("space-ser"),
            Source: new NeuronAddress(new OrganizationId("org-ser"), new SpaceId("space-ser"), "chat.group.v1", "chat-ser"),
            SourceSequence: 4,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.Parse("2026-07-18T12:00:00Z"));

        var command = new CommandSynapse<StartDiscussion>(
            metadata,
            new StartDiscussion("serialize-me", "gpt-key", "grok-key"));
        var receipt = new CommandReceipt(metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null);
        var surface = new UiSurface("surface", 1, [new UiBlock("text", "body", [])]);

        var deserialized = serializer.Deserialize<CommandSynapse<StartDiscussion>>(serializer.SerializeToArray(command));
        Assert.Equal(command.Metadata.CommandId, deserialized.Metadata.CommandId);
        Assert.Equal(command.Payload.Topic, deserialized.Payload.Topic);
        Assert.Equal(command.Payload.GptKey, deserialized.Payload.GptKey);
        Assert.Equal(receipt, deepCopier.Copy(receipt));

        var copiedSurface = deepCopier.Copy(surface);
        Assert.Equal(surface.SurfaceId, copiedSurface.SurfaceId);
        Assert.Equal(surface.Revision, copiedSurface.Revision);
        Assert.Equal(surface.Blocks.Count, copiedSurface.Blocks.Count);
        Assert.Equal(surface.Blocks[0].Text, copiedSurface.Blocks[0].Text);
        Assert.Equal(metadata.Source, deepCopier.Copy(metadata.Source));
    }

    [Fact]
    public void Contracts_assembly_references_no_forbidden_sdk()
    {
        var assembly = typeof(OrganizationId).Assembly;
        var referenced = assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.Agents", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.Contains("ModelContextProtocol", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Azure.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.Contains("Google.Apis", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.Contains("Salesforce", StringComparison.OrdinalIgnoreCase) && name != assembly.GetName().Name);
        Assert.Contains(referenced, name => name is "Orleans.Core.Abstractions" or "Microsoft.Orleans.Core.Abstractions" or "Orleans.Serialization.Abstractions");
    }
}
