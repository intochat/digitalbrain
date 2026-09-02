using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class ContractOwnershipTests
{
    [Fact]
    public void ProductInteractionTypesDoNotBelongToKernelContracts()
    {
        Assert.NotSame(typeof(INeuron).Assembly, typeof(CommandId).Assembly);
        Assert.Same(typeof(CommandId).Assembly, typeof(AgentTurnContext).Assembly);
        Assert.Equal("DigitalBrain.Product.Identity", typeof(CommandId).Namespace);
        Assert.Equal("DigitalBrain.Product.Interactions", typeof(AgentTurnContext).Namespace);
        Assert.DoesNotContain(
            typeof(INeuron).Assembly.GetTypes(),
            static type => type.Namespace?.Contains(".Interactions", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProductContractWireIdentityRemainsStable()
    {
        var productAssembly = typeof(CommandId).Assembly;
        var publicTypes = new (Type Type, string Namespace)[]
        {
            (typeof(CommandId), "DigitalBrain.Product.Identity"),
            (typeof(AgentTurnContext), "DigitalBrain.Product.Interactions"),
            (typeof(UserActionRequest), "DigitalBrain.Product.Interactions"),
            (typeof(ITrustedUserCommandHandler), "DigitalBrain.Product.Interactions"),
            (typeof(IUntrustedContentScreen), "DigitalBrain.Product.Interactions"),
            (typeof(IUserActionContinuation), "DigitalBrain.Product.Interactions"),
            (typeof(IUserActionSource), "DigitalBrain.Product.Interactions"),
        };

        Assert.All(publicTypes, contract =>
        {
            Assert.Same(productAssembly, contract.Type.Assembly);
            Assert.Equal(contract.Namespace, contract.Type.Namespace);
        });

        AssertAlias<CommandId>("db.command-id");
        AssertAlias<AgentTurnContext>("db.agent-turn-context");
        AssertAlias<UserActionRequest>("db.user-action-request");

        AssertField<CommandId>(nameof(CommandId.Value), 0, typeof(Guid));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.Chat), 0, typeof(NeuronId));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.CommandId), 1, typeof(CommandId));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.Actor), 2, typeof(ActorContext));
        AssertField<AgentTurnContext>(nameof(AgentTurnContext.AllowedToolNames), 3, typeof(string[]));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Id), 0, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Provider), 1, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.DisplayName), 2, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.Message), 3, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.LoginUrl), 4, typeof(string));
        AssertField<UserActionRequest>(nameof(UserActionRequest.ExpiresAt), 5, typeof(DateTimeOffset));
        AssertField<UserActionRequest>(nameof(UserActionRequest.ResumeToolNames), 6, typeof(string[]));

        var kernelContracts = typeof(INeuron).Assembly;
        Assert.Null(kernelContracts.GetType("DigitalBrain.Abstractions.Identity.CommandId"));
        Assert.Null(kernelContracts.GetType("DigitalBrain.Abstractions.Interactions.AgentTurnContext"));
        Assert.Null(kernelContracts.GetType("DigitalBrain.Abstractions.Interactions.UserActionRequest"));
    }

    [Fact]
    public void AgentTurnContextEnterRestoresNestedContextAndDisposeIsIdempotent()
    {
        var previous = AgentTurnContext.Current;
        var owner = new OwnerId("owner");
        var actor = new ActorContext(PrincipalId.New(), "user");
        var outer = new AgentTurnContext(
            new NeuronId("chat", owner, "outer"),
            CommandId.New(),
            actor);
        var inner = new AgentTurnContext(
            new NeuronId("chat", owner, "inner"),
            CommandId.New(),
            actor,
            ["tool"]);

        using (AgentTurnContext.Enter(outer))
        {
            Assert.Same(outer, AgentTurnContext.Current);
            var innerScope = AgentTurnContext.Enter(inner);
            Assert.Same(inner, AgentTurnContext.Current);

            innerScope.Dispose();
            innerScope.Dispose();

            Assert.Same(outer, AgentTurnContext.Current);
        }

        Assert.Same(previous, AgentTurnContext.Current);
    }

    private static void AssertAlias<T>(string expected)
    {
        var alias = Assert.Single(
            typeof(T).GetCustomAttributesData(),
            static attribute => attribute.AttributeType.Name == "AliasAttribute");

        Assert.Equal(expected, Assert.Single(alias.ConstructorArguments).Value);
    }

    private static void AssertField<T>(string propertyName, int expectedId, Type expectedType)
    {
        var property = typeof(T).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property.PropertyType);
        var id = Assert.Single(
            property.GetCustomAttributesData(),
            static attribute => attribute.AttributeType.Name == "IdAttribute");

        Assert.Equal(expectedId, Convert.ToInt32(Assert.Single(id.ConstructorArguments).Value));
    }
}
