using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AIContracts
{
    private static readonly Assembly Runtime = typeof(AIModule).Assembly;

    [Fact(DisplayName = "LLM and Agent expose only the frozen immutable MEAI request boundary")]
    public void AiNeuronContractsExposeOnlyTheFrozenMeaiBoundary()
    {
        foreach (var contract in new[] { typeof(ILLM), typeof(IAgent) })
        {
            var method = Assert.Single(contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            var parameter = Assert.Single(method.GetParameters());

            Assert.Equal("RespondAsync", method.Name);
            Assert.Equal(typeof(Task<ChatResponse>), method.ReturnType);
            Assert.Equal(typeof(IReadOnlyList<ChatMessage>), parameter.ParameterType);
            Assert.DoesNotContain(method.GetParameters(), candidate => candidate.ParameterType == typeof(ChatOptions));
        }

        Assert.False(typeof(IAgent).IsAssignableFrom(typeof(ILLM)));
    }

    [Fact(DisplayName = "GroupChat is both an Agent and a Task Worker")]
    public void GroupChatCombinesAgentAndWorkerContracts()
    {
        Assert.Contains(typeof(IAgent), typeof(IGroupChat).GetInterfaces());
        Assert.Contains(typeof(IWorker), typeof(IGroupChat).GetInterfaces());
    }

    [Fact(DisplayName = "GroupChat owns exact protected Task mapping hooks and concrete short Worker methods")]
    public void GroupChatOwnsTaskMappingAndWorkerExecutionBoundary()
    {
        var messages = Assert.Single(
            typeof(GroupChat).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "CreateMessages");
        var result = Assert.Single(
            typeof(GroupChat).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "CreateResult");

        Assert.True(messages.IsFamily);
        Assert.True(messages.IsAbstract);
        Assert.Equal(typeof(IReadOnlyList<ChatMessage>), messages.ReturnType);
        Assert.Equal(typeof(Goal), Assert.Single(messages.GetParameters()).ParameterType);
        Assert.True(result.IsFamily);
        Assert.True(result.IsAbstract);
        Assert.Equal(typeof(Result), result.ReturnType);
        Assert.Equal(typeof(IReadOnlyList<ChatMessage>), Assert.Single(result.GetParameters()).ParameterType);

        var accept = Assert.Single(
            typeof(GroupChat).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            candidate => candidate.Name == "AcceptAsync");
        Assert.False(accept.IsAbstract);

        foreach (var methodName in new[] { "ContinueAsync", "CancelAsync" })
        {
            var deferred = Assert.Single(
                typeof(GroupChat).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                candidate => candidate.Name == methodName);

            Assert.True(deferred.IsAbstract);
        }
    }

    [Fact(DisplayName = "the workflow runner is private non-neuron Orleans infrastructure")]
    public void WorkflowRunnerIsPrivateNonNeuronInfrastructure()
    {
        var runner = Runtime.GetType("DigitalBrain.AI.WorkflowRunner", throwOnError: false);

        Assert.NotNull(runner);
        Assert.False(runner.IsPublic);
        Assert.True(typeof(Grain).IsAssignableFrom(runner));
        Assert.False(typeof(INeuron).IsAssignableFrom(runner));
        Assert.DoesNotContain(Runtime.GetExportedTypes(), type => type.Name.Contains("WorkflowRunner", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "supervised AI wire aliases and field ids are pinned")]
    public void SupervisedAiWireVocabularyIsPinned()
    {
        var expected = new Dictionary<string, (string Alias, (string Name, uint Id)[] Members)>(StringComparer.Ordinal)
        {
            ["DigitalBrain.AI.AIWorkerState"] = ("ai.worker-state", [("Cursor", 0u), ("ReplayInput", 1u), ("Definition", 2u), ("Checkpoint", 3u), ("Causation", 4u), ("ActiveRun", 5u)]),
            ["DigitalBrain.AI.WorkflowCheckpointReference"] = ("ai.workflow-checkpoint-reference", [("SessionId", 0u), ("CheckpointId", 1u)]),
            ["DigitalBrain.AI.WorkflowRun"] = ("ai.workflow-run", [("RunId", 0u), ("Cursor", 1u), ("DefinitionFingerprint", 2u), ("InputCheckpoint", 3u), ("RecoverAfterUtc", 4u)]),
            ["DigitalBrain.AI.WorkflowRunCommand"] = ("ai.workflow-run-command", [("Run", 0u), ("Definition", 1u), ("ReplayInput", 2u)]),
            ["DigitalBrain.AI.WorkflowRunResult"] = ("ai.workflow-run-result", [("Run", 0u), ("OutputCheckpoint", 1u), ("TerminalMessages", 2u)]),
            ["DigitalBrain.AI.OrchestrationParticipant"] = ("ai.orchestration-participant", [("Contract", 0u), ("NeuronId", 1u), ("AgentId", 2u), ("AgentName", 3u)]),
            ["DigitalBrain.AI.OrchestrationDefinition"] = ("ai.orchestration-definition", [("FormatVersion", 0u), ("MafVersion", 1u), ("Fingerprint", 2u), ("Participants", 3u), ("HostId", 4u), ("HostName", 5u)]),
            ["DigitalBrain.AI.CheckpointWrite"] = ("ai.checkpoint-write", [("SessionId", 0u), ("ProtectedPayload", 1u), ("Parent", 2u)]),
        };
        var aliases = new List<string>();

        foreach (var (name, contract) in expected)
        {
            var type = Assert.IsType<Type>(Runtime.GetType(name, throwOnError: false), exactMatch: false);

            Assert.Equal(contract.Alias, type.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
            Assert.Equal(contract.Members, SerializedMembers(type));
            aliases.Add(contract.Alias);
        }

        var interfaces = new Dictionary<string, (string Alias, string[] Methods)>(StringComparer.Ordinal)
        {
            ["DigitalBrain.AI.IWorkflowRunner"] = ("ai.workflow-runner", ["Execute"]),
            ["DigitalBrain.AI.IWorkflowRunOwner"] = ("ai.workflow-run-owner", ["AuthorizeParticipant", "AuthorizeCompletion"]),
            ["DigitalBrain.AI.IWorkflowRunCompletion"] = ("ai.workflow-run-completion", ["Complete"]),
            ["DigitalBrain.AI.IWorkflowCheckpointGrain"] = ("ai.workflow-checkpoint-grain", ["Create", "Read", "Index"]),
        };

        foreach (var (name, contract) in interfaces)
        {
            var type = Assert.IsType<Type>(Runtime.GetType(name, throwOnError: false), exactMatch: false);

            Assert.Equal(contract.Alias, type.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(
                contract.Methods,
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(method => method.GetCustomAttribute<AliasAttribute>()?.Alias)
                    .ToArray());
            aliases.Add(contract.Alias);
        }

        Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(DisplayName = "workflow checkpoint identity uses exact Worker, Task and Attempt while excluding revision")]
    public void WorkflowCheckpointIdentityUsesOnlyWorkerTaskAndAttempt()
    {
        var identity = Assert.IsType<Type>(
            Runtime.GetType("DigitalBrain.AI.WorkflowCheckpointIdentity", throwOnError: false),
            exactMatch: false);
        var create = Assert.Single(
            identity.GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name == "For");
        Assert.Equal(typeof(AttemptCursor), Assert.Single(create.GetParameters()).ParameterType);
        var owner = new OwnerId("checkpoint-identity-contract");
        var task = NeuronId.For<ITask>(owner, "task");
        var worker = NeuronId.For<IGroupChat>(owner, "worker");
        var attempt = new AttemptId(Guid.NewGuid());
        var first = create.Invoke(null, [new AttemptCursor(task, worker, attempt, Revision: 0)]);
        var revised = create.Invoke(null, [new AttemptCursor(task, worker, attempt, Revision: 91)]);
        var differentAttempt = create.Invoke(
            null,
            [new AttemptCursor(task, worker, new AttemptId(Guid.NewGuid()), Revision: 0)]);
        var differentTask = create.Invoke(
            null,
            [new AttemptCursor(NeuronId.For<ITask>(owner, "other-task"), worker, attempt, Revision: 0)]);
        var differentWorker = create.Invoke(
            null,
            [new AttemptCursor(task, NeuronId.For<IGroupChat>(owner, "other-worker"), attempt, Revision: 0)]);

        Assert.NotNull(first);
        Assert.Equal(first, revised);
        Assert.NotEqual(first, differentAttempt);
        Assert.NotEqual(first, differentTask);
        Assert.NotEqual(first, differentWorker);
    }

    private static (string Name, uint Id)[] SerializedMembers(Type type)
        => type.GetMembers(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly)
            .Where(member => member is FieldInfo or PropertyInfo)
            .Select(member => (member.Name, Id: member.GetCustomAttribute<IdAttribute>()?.Id))
            .Where(member => member.Id.HasValue)
            .OrderBy(member => member.Id)
            .Select(member => (member.Name, member.Id!.Value))
            .ToArray();

    [Fact(DisplayName = "model identity is expressed by its namespace and concrete neuron type")]
    public void ModelIdentityIsTheType()
    {
        Assert.Equal("DigitalBrain.AI.Ollama", typeof(Llama32).Namespace);
        Assert.Equal("DigitalBrain.AI.OpenAI", typeof(Gpt56).Namespace);
        Assert.Equal(typeof(LLM), typeof(Llama32).BaseType);
        Assert.Equal(typeof(LLM), typeof(Gpt56).BaseType);
        Assert.Contains(typeof(ILlama32), typeof(Llama32).GetInterfaces());
        Assert.Contains(typeof(IGpt56), typeof(Gpt56).GetInterfaces());
    }

    [Fact(DisplayName = "a concrete LLM receives only the chat client keyed by its own type")]
    public void ModelConstructorCarriesItsTypedLlmKey()
    {
        var parameter = Assert.Single(Assert.Single(typeof(Llama32).GetConstructors()).GetParameters());
        var binding = Assert.IsAssignableFrom<FromKeyedServicesAttribute>(
            Assert.Single(parameter.GetCustomAttributes(inherit: false)));

        Assert.Equal(typeof(IChatClient), parameter.ParameterType);
        Assert.Equal(typeof(Llama32), binding.Key);
    }

    [Fact(DisplayName = "AIModule registers each chat client by its concrete LLM neuron type")]
    public void ModuleRegistersTypedChatClients()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseOrleans(AIModule.Configure);

        using var host = builder.Build();
        var llama = host.Services.GetRequiredKeyedService<IChatClient>(typeof(Llama32));

        Assert.NotNull(llama);
        Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredKeyedService<IChatClient>(typeof(Gpt56)));
    }

    [Fact(DisplayName = "AIModule preserves the host Data Protection application discriminator")]
    public void ModulePreservesHostDataProtectionIsolation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDataProtection().SetApplicationName("brain-deployment");
        builder.UseOrleans(AIModule.Configure);

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal("brain-deployment", options.ApplicationDiscriminator);
    }

    [Fact(DisplayName = "IChatClient injection is confined to concrete LLM neurons")]
    public void ChatClientInjectionIsConfinedToConcreteModels()
    {
        var consumers = Runtime.GetTypes()
            .Where(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IChatClient)))
            .ToArray();

        Assert.Equal([typeof(Gpt56), typeof(Llama32)], consumers.OrderBy(type => type.Name));
        Assert.All(consumers, type => Assert.Equal(typeof(LLM), type.BaseType));
    }

    [Fact(DisplayName = "every concrete LLM follows the namespace, contract and typed-key grammar")]
    public void ConcreteModelsFollowTheTypeGrammar()
    {
        var models = Runtime.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(LLM)))
            .ToArray();

        Assert.NotEmpty(models);

        foreach (var model in models)
        {
            Assert.StartsWith("DigitalBrain.AI.", model.Namespace, StringComparison.Ordinal);
            Assert.Contains(
                model.GetInterfaces(),
                contract => contract.Namespace == model.Namespace
                    && contract.Name == $"I{model.Name}"
                    && typeof(ILLM).IsAssignableFrom(contract));

            var parameter = Assert.Single(Assert.Single(model.GetConstructors()).GetParameters());
            var binding = Assert.Single(
                parameter.GetCustomAttributes(inherit: false).OfType<FromKeyedServicesAttribute>());

            Assert.Equal(typeof(IChatClient), parameter.ParameterType);
            Assert.Equal(model, binding.Key);
        }
    }

    [Fact(DisplayName = "AI contracts expose MEAI abstractions without provider or MAF types")]
    public void ContractsExposeOnlyMeaiAbstractions()
    {
        var contracts = typeof(ILLM).Assembly;
        var references = contracts.GetReferencedAssemblies();
        var surface = contracts.GetExportedTypes().SelectMany(type => type.GetMembers()).ToArray();

        Assert.Contains(references, reference => reference.Name == "Microsoft.Extensions.AI.Abstractions");
        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) is true);
        Assert.DoesNotContain(references, reference => reference.Name == "Microsoft.Extensions.AI.OpenAI");
        Assert.DoesNotContain(references, reference => reference.Name == "OllamaSharp");
        Assert.DoesNotContain(surface, member => member.ToString()?.Contains("Microsoft.Agents", StringComparison.Ordinal) is true);
    }
}
