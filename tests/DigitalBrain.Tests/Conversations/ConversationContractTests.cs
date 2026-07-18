using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

public sealed class ConversationContractTests
{
    [Fact]
    public void Conversation_contracts_are_orleans_serializable()
    {
        foreach (var contractType in new[]
                 {
                     typeof(ConversationId),
                     typeof(ConversationTurnId),
                     typeof(ConversationTurnRequest),
                     typeof(ConversationTurnResult),
                     typeof(ConversationTurn),
                     typeof(ConversationSnapshot)
                 })
            Assert.True(
                contractType.GetCustomAttributes(typeof(GenerateSerializerAttribute), inherit: false).Length == 1,
                $"{contractType.Name} must carry [GenerateSerializer].");
    }

    [Fact]
    public void Conversation_neuron_exposes_only_submit_and_read()
    {
        var methods = typeof(IConversationNeuron)
            .GetMethods()
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(IConversationNeuron.ReadAsync), nameof(IConversationNeuron.SubmitTurnAsync)], methods);
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(IConversationNeuron)));
        Assert.DoesNotContain(methods, name => name.Contains("Ask", StringComparison.Ordinal));
        Assert.DoesNotContain(methods, name => name.Contains("Json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, name => name.Contains("Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public void Conversation_neuron_methods_use_typed_contracts_only()
    {
        var submit = typeof(IConversationNeuron).GetMethod(nameof(IConversationNeuron.SubmitTurnAsync))!;
        var read = typeof(IConversationNeuron).GetMethod(nameof(IConversationNeuron.ReadAsync))!;

        Assert.Equal(typeof(Task<ConversationTurnResult>), submit.ReturnType);
        Assert.Equal(typeof(ConversationTurnRequest), Assert.Single(submit.GetParameters()).ParameterType);
        Assert.Equal(typeof(Task<ConversationSnapshot>), read.ReturnType);
        Assert.Empty(read.GetParameters());

        foreach (var method in new[] { submit, read })
        {
            var alias = Assert.IsType<AliasAttribute>(
                Assert.Single(method.GetCustomAttributes(typeof(AliasAttribute), inherit: false)));
            Assert.Equal(method.Name, alias.Alias);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\ncontrol")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void Invalid_conversation_ids_are_rejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ConversationId(value));
    }

    [Fact]
    public void Overlong_conversation_ids_are_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ConversationId(new string('c', 257)));
    }

    [Fact]
    public void Valid_conversation_id_round_trips_its_value()
    {
        Assert.Equal("main-chat", new ConversationId("main-chat").Value);
    }

    [Fact]
    public void Empty_turn_id_is_rejected_and_new_turn_ids_are_stable()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ConversationTurnId(Guid.Empty));

        var turnId = ConversationTurnId.New();
        Assert.Equal(turnId, new ConversationTurnId(turnId.Value));
    }

    [Fact]
    public void Turn_request_rejects_default_turn_identity()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ConversationTurnRequest(default, ConversationRole.Fast, "hello"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Turn_request_rejects_empty_text(string text)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ConversationTurnRequest(ConversationTurnId.New(), ConversationRole.Fast, text));
    }

    [Fact]
    public void Turn_request_carries_turn_identity_role_and_text()
    {
        var turnId = ConversationTurnId.New();

        var request = new ConversationTurnRequest(turnId, ConversationRole.Balanced, "hello");

        Assert.Equal(turnId, request.TurnId);
        Assert.Equal(ConversationRole.Balanced, request.Role);
        Assert.Equal("hello", request.Text);
    }

    [Fact]
    public void Conversation_roles_are_exactly_fast_balanced_reasoning()
    {
        Assert.Equal(
            [ConversationRole.Fast, ConversationRole.Balanced, ConversationRole.Reasoning],
            Enum.GetValues<ConversationRole>());
    }

    [Fact]
    public void Conversation_contracts_are_excluded_from_the_one_per_owner_quadrant_catalog()
    {
        var registrations = DigitalBrain.Kernel.NeuronTypeCatalogBuilder.Build(
        [
            typeof(INeuron),
            typeof(IConversationNeuron),
            typeof(Tests.Conversations.ITestConversationNeuron),
            typeof(Tests.Conversations.TestConversationNeuron)
        ]);

        Assert.Empty(registrations);
    }
}
