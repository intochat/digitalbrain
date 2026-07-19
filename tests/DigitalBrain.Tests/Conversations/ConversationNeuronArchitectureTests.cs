using DigitalBrain;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

public sealed class ConversationNeuronArchitectureTests
{
    [Fact]
    public void Production_conversation_grain_derives_the_durable_neuron_and_implements_the_internal_marker()
    {
        Assert.True(typeof(Neuron).IsAssignableFrom(typeof(ConversationNeuron)));
        Assert.True(typeof(IConversationNeuron).IsAssignableFrom(typeof(ConversationNeuron)));
        Assert.True(typeof(IConversationGrain).IsAssignableFrom(typeof(ConversationNeuron)));
        Assert.False(typeof(ConversationNeuron).IsAbstract);
    }

    [Fact]
    public void Conversation_state_is_derived_without_a_fourth_universal_neuron_member()
    {
        Assert.Equal(
            [
                nameof(NeuronDurableState.Operations),
                nameof(NeuronDurableState.Outbox),
                nameof(NeuronDurableState.Status)
            ],
            typeof(NeuronDurableState)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                nameof(ConversationDurableState.Intents),
                nameof(ConversationDurableState.Results),
                nameof(ConversationDurableState.Revision),
                nameof(ConversationDurableState.Turns)
            ],
            typeof(ConversationDurableState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(
            typeof(ConversationDurableState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            property => Assert.True(
                property.PropertyType.IsGenericType &&
                (property.PropertyType.GetGenericTypeDefinition() == typeof(IDurableDictionary<,>) ||
                 property.PropertyType.GetGenericTypeDefinition() == typeof(IDurableValue<>))));
    }

    [Fact]
    public void Conversation_journal_types_are_registered_in_the_official_json_journal_context()
    {
        Assert.NotNull(NeuronJournalJsonContext.Default.GetTypeInfo(typeof(ConversationTurnRequest)));
        Assert.NotNull(NeuronJournalJsonContext.Default.GetTypeInfo(typeof(ConversationTurn)));
        Assert.NotNull(NeuronJournalJsonContext.Default.GetTypeInfo(typeof(ConversationTurnResult)));
    }

    [Fact]
    public void Conversation_turn_request_round_trips_through_the_official_json_journal_context()
    {
        var request = new ConversationTurnRequest(
            new ConversationTurnId(Guid.Parse("8b842fc5-ae58-4592-b5cb-d311160037ec")),
            ConversationRole.Balanced,
            "recover this turn");

        var json = JsonSerializer.Serialize(
            request,
            typeof(ConversationTurnRequest),
            NeuronJournalJsonContext.Default);
        Assert.Contains(
            request.TurnId.Value.ToString(),
            json,
            StringComparison.OrdinalIgnoreCase);
        var recovered = Assert.IsType<ConversationTurnRequest>(
            JsonSerializer.Deserialize(
                json,
                typeof(ConversationTurnRequest),
                NeuronJournalJsonContext.Default));

        Assert.Equal(request, recovered);
    }

    [Fact]
    public void Kernel_assembly_contains_no_custom_or_canned_IChatClient_implementation()
    {
        var customChatClients = typeof(ConversationNeuron).Assembly
            .GetTypes()
            .Where(type => type.IsClass && typeof(IChatClient).IsAssignableFrom(type))
            .ToArray();

        Assert.Empty(customChatClients);
    }

    [Fact]
    public void Provider_SDK_types_do_not_escape_through_public_conversation_contracts()
    {
        var contractTypes = typeof(IConversationNeuron)
            .GetMethods()
            .SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType))
            .SelectMany(Flatten)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(
            contractTypes,
            type =>
                type.Namespace?.StartsWith("OpenAI", StringComparison.Ordinal) == true ||
                type.Namespace?.StartsWith("Anthropic", StringComparison.Ordinal) == true ||
                type == typeof(IChatClient));
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
            foreach (var nested in Flatten(argument))
                yield return nested;
    }
}
