extern alias McpProject;

using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using ConversationStateClient = McpProject::DigitalBrain.Mcp.ConversationStateClient;
using McpInoCommandHandler = McpProject::DigitalBrain.Mcp.McpInoCommandHandler;

namespace DigitalBrain.Tests.Runtime;

public sealed class LegacyInoPipelineRemovalTests
{
    [Fact]
    public void Ino_command_handler_exposes_only_the_durable_acceptance_boundary()
    {
        var constructor = Assert.Single(typeof(McpInoCommandHandler).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(ConversationStateClient), parameter.ParameterType);
        Assert.Null(typeof(McpInoCommandHandler).GetMethod("ExecuteAsync"));
        var acceptance = Assert.Single(typeof(McpInoCommandHandler).GetMethods(), method => method.Name == "AcceptAsync");
        Assert.Single(acceptance.GetParameters());
    }

    [Fact]
    public void Legacy_raw_oauth_continuation_contracts_are_not_part_of_the_durable_ino_surface()
    {
        var assembly = typeof(InoConversationSnapshot).Assembly;

        Assert.Null(assembly.GetType("DigitalBrain.Core.Runtime.ExternalAuthorizationContinuation"));
        Assert.Null(assembly.GetType("DigitalBrain.Core.Runtime.ExternalAuthorizationWait"));
        Assert.Null(assembly.GetType("DigitalBrain.Core.Runtime.CommandExecutionAttempt"));
    }

    [Fact]
    public void Legacy_v2_effect_execution_rail_is_not_part_of_the_runtime_surface()
    {
        var core = typeof(WorkflowReference).Assembly;
        var abstractions = typeof(IConversationNeuron).Assembly;
        var kernel = typeof(InoOperationWorkerGrain).Assembly;

        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.IAggregateStore"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.InMemoryAggregateStore"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.EffectCoordinator"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.IEffectHandler"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.IEffectVerifier"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.OutboxRecord"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.EffectTransitionRecord"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.ICommandHandler"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.CommandExecutionResult"));
        Assert.Null(abstractions.GetType("DigitalBrain.Kernel.Runtime.IAggregateGrain"));
        Assert.Null(abstractions.GetType("DigitalBrain.Kernel.Runtime.IEffectWorkerGrain"));
        Assert.Null(abstractions.GetType("DigitalBrain.Kernel.Runtime.IEffectWorkerPort"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.Runtime.AggregateGrain"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.Runtime.EffectCommandHandler"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.Runtime.EffectWorkerGrain"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.OrleansAggregateStore"));
        Assert.Null(core.GetType("DigitalBrain.Core.Runtime.IInoToolGateway"));
        Assert.Null(abstractions.GetType("DigitalBrain.Kernel.Runtime.IInoOperationCapability"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.Runtime.PlanInoToolGateway"));
        Assert.Null(kernel.GetType("DigitalBrain.Kernel.Runtime.ClosedInoToolGateway"));
    }

    [Fact]
    public void Conversation_grain_exposes_only_atomic_lease_fenced_lifecycle_mutations()
    {
        var methods = typeof(IConversationNeuron).GetMethods().Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("PutOperationAsync", methods);
        Assert.DoesNotContain("SuspendAuthorizationAsync", methods);
        Assert.DoesNotContain("CompleteOperationAsync", methods);
        Assert.DoesNotContain("EnqueueOutboxAsync", methods);
    }
}
