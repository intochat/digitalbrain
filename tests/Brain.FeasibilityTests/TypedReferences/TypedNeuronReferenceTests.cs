using System.Reflection;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Brain.FeasibilityTests.TypedReferences;

public sealed class TypedNeuronReferenceTests : IClassFixture<TypedOrleansClusterFixture>
{
    private readonly TypedOrleansClusterFixture _fixture;

    public TypedNeuronReferenceTests(TypedOrleansClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public void Brain_get_returns_real_typed_grain_reference()
    {
        var brain = new TypedBrain(_fixture.Cluster.GrainFactory);
        var agent = brain.Get<IGpt56>("org-1", "space-1", "gpt-1");

        Assert.NotNull(agent);
        Assert.IsAssignableFrom<IGpt56>(agent);
        Assert.IsAssignableFrom<IGrain>(agent);
        Assert.IsAssignableFrom<IAddressable>(agent);

        var grainId = ((IAddressable)agent).GetGrainId();
        var expectedKey = NeuronIdentity.Derive(typeof(IGpt56), "org-1", "space-1", "gpt-1");
        Assert.Equal(expectedKey, grainId.Key.ToString());
        Assert.DoesNotContain("DispatchProxy", agent.GetType().FullName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Group_chat_receives_typed_agent_grain_references()
    {
        var brain = new TypedBrain(_fixture.Cluster.GrainFactory);
        var gpt = brain.Get<IGpt56>("org-1", "space-1", "gpt-1");
        var grok = brain.Get<IGrok45>("org-1", "space-1", "grok-1");
        var chat = brain.Get<IGroupChat>("org-1", "space-1", "chat-1");

        await chat.SetParticipantsAsync([gpt, grok]);
        var participants = await chat.GetParticipantsAsync();

        Assert.Equal(2, participants.Count);
        Assert.IsAssignableFrom<IAgent>(participants[0]);
        Assert.IsAssignableFrom<IAgent>(participants[1]);
        Assert.Equal(((IAddressable)gpt).GetGrainId(), ((IAddressable)participants[0]).GetGrainId());
        Assert.Equal(((IAddressable)grok).GetGrainId(), ((IAddressable)participants[1]).GetGrainId());
        Assert.Equal("org-1|space-1|agent.gpt56.v1/gpt-1", await participants[0].GetIdentityAsync());
        Assert.Equal("org-1|space-1|agent.grok45.v1/grok-1", await participants[1].GetIdentityAsync());
    }

    [Fact]
    public async Task Typed_grain_reference_round_trip_preserves_identity()
    {
        var brain = new TypedBrain(_fixture.Cluster.GrainFactory);
        var gpt = brain.Get<IGpt56>("org-a", "space-b", "instance-c");
        var chat = brain.Get<IGroupChat>("org-a", "space-b", "chat-round");

        var originalId = ((IAddressable)gpt).GetGrainId();
        await chat.SetParticipantsAsync([gpt]);
        var roundTripped = (await chat.GetParticipantsAsync())[0];
        var roundTrippedId = ((IAddressable)roundTripped).GetGrainId();

        Assert.Equal(originalId, roundTrippedId);
        Assert.Equal(originalId.Key.ToString(), await roundTripped.GetIdentityAsync());
        Assert.Equal(
            NeuronIdentity.Derive(typeof(IGpt56), "org-a", "space-b", "instance-c"),
            originalId.Key.ToString());
    }

    [Fact]
    public void Resolver_contains_no_dispatch_proxy_or_dynamic_invocation()
    {
        var resolverType = typeof(TypedBrain);
        Assert.False(typeof(DispatchProxy).IsAssignableFrom(resolverType));
        Assert.DoesNotContain("DispatchProxy", resolverType.BaseType?.FullName ?? string.Empty, StringComparison.Ordinal);

        foreach (var method in resolverType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain("Invoke", method.Name, StringComparison.OrdinalIgnoreCase);
        }

        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "TypedReferences", "TypedBrain.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));
        Assert.DoesNotContain("DispatchProxy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppDomain", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAssemblies", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.Load", source, StringComparison.Ordinal);
        Assert.Contains("GetGrain<", source, StringComparison.Ordinal);
    }
}
