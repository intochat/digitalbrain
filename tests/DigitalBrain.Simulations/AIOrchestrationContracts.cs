using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AIOrchestrationContracts
{
    [Fact(DisplayName = "Concurrent asks typed models independently with the same immutable input")]
    public async Task ConcurrentModelsRemainIndependent()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = new OrchestrationChatClient("gpt-independent");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("concurrent-models");
            var panelId = NeuronId.For<ITestConcurrent>(owner, "panel");
            var probeId = NeuronId.For<IAIOrchestrationProbe>(owner, "probe");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(probeId.ToGrainId());
            ChatMessage[] request =
            [
                new(ChatRole.System, "shared-system"),
                new(ChatRole.User, "shared-question")
            ];

            var response = await probe.CallAsync(panelId, request);
            var panelIncoming = await probe.ReadJournalAsync(panelId, JournalKind.Incoming);
            var panelOutgoing = await probe.ReadJournalAsync(panelId, JournalKind.Outgoing);
            var llamaId = NeuronId.For<ILlama32>(owner, "panel");
            var gptId = NeuronId.For<IGpt56>(owner, "panel");
            var llamaIncoming = await probe.ReadJournalAsync(llamaId, JournalKind.Incoming);
            var gptIncoming = await probe.ReadJournalAsync(gptId, JournalKind.Incoming);

            var llamaCall = Assert.Single(llama.Calls);
            var gptCall = Assert.Single(gpt.Calls);
            var outerRequest = Assert.Single(
                panelIncoming.Delta,
                delivery => delivery.Synapse is CapabilityRequested);
            var childRequests = panelOutgoing.Delta
                .Where(delivery => delivery.Synapse is CapabilityRequested)
                .ToArray();

            Assert.Equal(["shared-system", "shared-question"], llamaCall.Select(message => message.Text));
            Assert.Equal(["shared-system", "shared-question"], gptCall.Select(message => message.Text));
            Assert.DoesNotContain(llamaCall, message => message.Text == "gpt-independent");
            Assert.DoesNotContain(gptCall, message => message.Text == "llama-independent");
            Assert.Contains("llama-independent", response.Text, StringComparison.Ordinal);
            Assert.Contains("gpt-independent", response.Text, StringComparison.Ordinal);
            Assert.Equal(2, childRequests.Length);
            AssertCapabilityRequest(
                Assert.Single(childRequests, delivery => ((CapabilityRequested)delivery.Synapse).Target == llamaId),
                outerRequest,
                panelId,
                llamaId,
                llamaIncoming);
            AssertCapabilityRequest(
                Assert.Single(childRequests, delivery => ((CapabilityRequested)delivery.Synapse).Target == gptId),
                outerRequest,
                panelId,
                gptId,
                gptIncoming);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Concurrent enters both typed participants before either is released")]
    public async Task ConcurrentParticipantsRunConcurrently()
    {
        var gate = new ConcurrentInvocationGate();
        using var llama = new OrchestrationChatClient(
            (_, cancellationToken) => gate.EnterAsync("llama-concurrent", cancellationToken));
        using var gpt = new OrchestrationChatClient(
            (_, cancellationToken) => gate.EnterAsync("gpt-concurrent", cancellationToken));
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("concurrent-execution");
            var panelId = NeuronId.For<ITestConcurrent>(owner, "panel");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());
            var responseTask = probe.CallAsync(
                panelId,
                [new ChatMessage(ChatRole.User, "run-together")]);

            try
            {
                await gate.BothEntered.WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                gate.Release();
            }

            var response = await responseTask;

            Assert.Equal(2, gate.Entered);
            Assert.Contains("llama-concurrent", response.Text, StringComparison.Ordinal);
            Assert.Contains("gpt-concurrent", response.Text, StringComparison.Ordinal);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct Concurrent rejects a foreign-owner participant before calls or state changes")]
    public async Task DirectConcurrentRejectsForeignOwnerParticipant()
    {
        using var llama = new OrchestrationChatClient("must-not-run");
        using var gpt = new OrchestrationChatClient("must-not-run");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("concurrent-owner-fence");
            var target = NeuronId.For<IForeignOwnerConcurrent>(owner, "panel");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());
            var before = await probe.ReadConcurrentStateAsync(target);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CallAsync(
                target,
                [new ChatMessage(ChatRole.User, "must-not-cross-owner")]));

            Assert.Contains("owner", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(llama.Calls);
            Assert.Empty(gpt.Calls);
            Assert.Equal(before, await probe.ReadConcurrentStateAsync(target));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct GroupChat rejects a foreign-owner participant before calls or state changes")]
    public async Task DirectGroupChatRejectsForeignOwnerParticipant()
    {
        using var llama = new OrchestrationChatClient("must-not-run");
        using var gpt = new OrchestrationChatClient("must-not-run");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("group-owner-fence");
            var target = NeuronId.For<IForeignOwnerGroupChat>(owner, "council");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());
            var before = await probe.ReadGroupStateAsync(target);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CallAsync(
                target,
                [new ChatMessage(ChatRole.User, "must-not-cross-owner")]));

            Assert.Contains("owner", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(llama.Calls);
            Assert.Empty(gpt.Calls);
            Assert.Equal(before, await probe.ReadGroupStateAsync(target));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Concurrent resumes one protected MAF session after deactivation")]
    public async Task ConcurrentResumesProtectedSessionAfterDeactivation()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = new OrchestrationChatClient("gpt-independent");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("concurrent-resume");
            var panelId = NeuronId.For<ITestConcurrent>(owner, "panel");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            _ = await probe.CallAsync(
                panelId,
                [new ChatMessage(ChatRole.User, "first-question")]);
            var protectedState = await probe.ReadConcurrentStateAsync(panelId);

            Assert.NotEmpty(protectedState);
            Assert.DoesNotContain(
                "first-question",
                System.Text.Encoding.UTF8.GetString(protectedState),
                StringComparison.Ordinal);

            await probe.DeactivateConcurrentAsync(panelId);
            _ = await probe.CallAsync(
                panelId,
                [new ChatMessage(ChatRole.User, "second-question")]);

            Assert.Equal(2, llama.Calls.Count);
            Assert.Equal(2, gpt.Calls.Count);
            Assert.Contains(llama.Calls[1], message => message.Text == "first-question");
            Assert.Contains(llama.Calls[1], message => message.Text == "second-question");
            Assert.Contains(gpt.Calls[1], message => message.Text == "first-question");
            Assert.Contains(gpt.Calls[1], message => message.Text == "second-question");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct Concurrent preserves prior real MAF session bytes when journal commit fails")]
    public async Task DirectConcurrentDoesNotAdvanceWhenSessionCommitFails()
    {
        using var llama = new OrchestrationChatClient("llama");
        using var gpt = new OrchestrationChatClient("gpt");
        var journals = new AIWorkerJournalStorageProvider();
        var cluster = await StartClusterAsync(llama, gpt, journals);

        try
        {
            var owner = new OwnerId("concurrent-write-failure");
            var panelId = NeuronId.For<ITestConcurrent>(owner, "panel");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            _ = await probe.CallAsync(
                panelId,
                [new ChatMessage(ChatRole.User, "establish-session")]);
            var before = await probe.ReadConcurrentStateAsync(panelId);
            Assert.NotEmpty(before);
            journals.FailWriteAfter(
                panelId.ToGrainId(),
                completedWritesBeforeFailure: 0,
                "injected direct-session write failure");

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CallAsync(
                panelId,
                [new ChatMessage(ChatRole.User, "must-not-commit")]));

            Assert.Contains("write failure", failure.Message, StringComparison.Ordinal);
            Assert.Equal(before, await probe.ReadConcurrentStateAsync(panelId));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "direct-session compatibility binds every stable execution input")]
    public void DirectSessionCompatibilityBindsEveryStableExecutionInput()
    {
        var owner = new OwnerId("definition-compatibility");
        (string Contract, NeuronId NeuronId, string AgentId, string AgentName)[] participants =
        [
            ("first-contract", new NeuronId("first", owner, "one"), "first-id", "first-name"),
            ("second-contract", new NeuronId("second", owner, "two"), "second-id", "second-name"),
        ];
        var baseline = CreateDefinition(
            "stable-orchestration-type",
            "group-chat",
            "maf/1",
            "in-process-lockstep",
            "round-robin:2",
            "none",
            participants);
        var byteIdenticalRebuild = CreateDefinition(
            "stable-orchestration-type",
            "group-chat",
            "maf/1",
            "in-process-lockstep",
            "round-robin:2",
            "none",
            participants);
        object[] incompatible =
        [
            WithDefinitionValue(baseline, "FormatVersion", 1),
            WithDefinitionValue(baseline, "Fingerprint", "changed-fingerprint"),
            WithDefinitionValue(baseline, "HostId", "changed-host-id"),
            WithDefinitionValue(baseline, "HostName", "changed-host-name"),
            CreateDefinition("stable-orchestration-type", "concurrent", "maf/1", "in-process-lockstep", "round-robin:2", "none", participants),
            CreateDefinition("stable-orchestration-type", "group-chat", "maf/1", "in-process-lockstep", "round-robin:2", "none", [participants[1], participants[0]]),
            CreateDefinition(
                "stable-orchestration-type",
                "group-chat",
                "maf/1",
                "in-process-lockstep",
                "round-robin:2",
                "none",
                [("changed-contract", participants[0].NeuronId, participants[0].AgentId, participants[0].AgentName), participants[1]]),
            CreateDefinition(
                "stable-orchestration-type",
                "group-chat",
                "maf/1",
                "in-process-lockstep",
                "round-robin:2",
                "none",
                [(participants[0].Contract, new NeuronId("changed", owner, "one"), participants[0].AgentId, participants[0].AgentName), participants[1]]),
            CreateDefinition(
                "stable-orchestration-type",
                "group-chat",
                "maf/1",
                "in-process-lockstep",
                "round-robin:2",
                "none",
                [(participants[0].Contract, participants[0].NeuronId, "changed-agent-id", participants[0].AgentName), participants[1]]),
            CreateDefinition(
                "stable-orchestration-type",
                "group-chat",
                "maf/1",
                "in-process-lockstep",
                "round-robin:2",
                "none",
                [(participants[0].Contract, participants[0].NeuronId, participants[0].AgentId, "changed-agent-name"), participants[1]]),
            CreateDefinition("changed-orchestration-type", "group-chat", "maf/1", "in-process-lockstep", "round-robin:2", "none", participants),
            CreateDefinition("stable-orchestration-type", "group-chat", "maf/2", "in-process-lockstep", "round-robin:2", "none", participants),
            CreateDefinition("stable-orchestration-type", "group-chat", "maf/1", "in-process-concurrent", "round-robin:2", "none", participants),
            WithDefinitionValue(baseline, "Manager", "round-robin:3"),
            CreateDefinition("stable-orchestration-type", "group-chat", "maf/1", "in-process-lockstep", "round-robin:2", "changed-aggregator", participants),
        ];

        Assert.Equal(2, Property<int>(baseline, "FormatVersion"));
        Assert.Equal(Fingerprint(baseline), Fingerprint(byteIdenticalRebuild));

        foreach (var changed in incompatible)
        {
            var failure = Assert.Throws<InvalidOperationException>(
                () => RequireDefinitionMatch(baseline, changed));

            Assert.Contains("migration", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reset", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "concrete direct definitions bind a stable application build identity")]
    public void ConcreteDefinitionsBindStableApplicationBuildIdentity()
    {
        var owner = new OwnerId("application-identity");
        Participant[] participants =
        [
            new Participant<ILlama32>(NeuronId.For<ILlama32>(owner, "model")),
            new Participant<IGpt56>(NeuronId.For<IGpt56>(owner, "model")),
        ];
        var informationalVersion = typeof(TestConcurrent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informationalVersion));
        var expectedVersion =
            $"{typeof(TestConcurrent).Assembly.GetName().Name}/{informationalVersion}";

        foreach (var factory in new[] { "CreateConcurrent", "CreateGroupChat" })
        {
            var orchestrationType = factory == "CreateConcurrent"
                ? typeof(TestConcurrent)
                : typeof(TestGroupChat);
            var first = DescribeShapeDefinition(factory, orchestrationType, participants);
            var repeated = DescribeShapeDefinition(factory, orchestrationType, participants);

            Assert.Equal(expectedVersion, Property<string>(first, "ApplicationVersion"));
            Assert.Equal(Fingerprint(first), Fingerprint(repeated));

            var drifted = WithDefinitionValue(
                first,
                "ApplicationVersion",
                $"{expectedVersion}-changed");
            var failure = Assert.Throws<InvalidOperationException>(
                () => RequireDefinitionMatch(first, drifted));

            Assert.Contains("migration", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reset", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "application identity and fingerprint ignore MVID rebuild noise")]
    public void ApplicationIdentityAndFingerprintIgnoreMvidRebuildNoise()
    {
        var owner = new OwnerId("mvid-independent-identity");
        Participant[] participants =
        [
            new Participant<ILlama32>(NeuronId.For<ILlama32>(owner, "model")),
        ];
        var firstType = CreateDynamicOrchestrationType("1.2.3+same-source");
        var rebuiltType = CreateDynamicOrchestrationType("1.2.3+same-source");
        var changedType = CreateDynamicOrchestrationType("1.2.4+different-source");

        Assert.Equal(firstType.AssemblyQualifiedName, rebuiltType.AssemblyQualifiedName);
        Assert.NotEqual(
            firstType.Module.ModuleVersionId,
            rebuiltType.Module.ModuleVersionId);

        var first = DescribeShapeDefinition("CreateConcurrent", firstType, participants);
        var rebuilt = DescribeShapeDefinition("CreateConcurrent", rebuiltType, participants);
        var changed = DescribeShapeDefinition("CreateConcurrent", changedType, participants);

        Assert.Equal(
            Property<string>(first, "ApplicationVersion"),
            Property<string>(rebuilt, "ApplicationVersion"));
        Assert.Equal(Fingerprint(first), Fingerprint(rebuilt));
        Assert.NotEqual(
            Property<string>(first, "ApplicationVersion"),
            Property<string>(changed, "ApplicationVersion"));
        var failure = Assert.Throws<InvalidOperationException>(
            () => RequireDefinitionMatch(first, changed));
        Assert.Contains("migration", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reset", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateDefinition(
        string orchestrationType,
        string kind,
        string mafVersion,
        string executionEnvironment,
        string manager,
        string aggregator,
        (string Contract, NeuronId NeuronId, string AgentId, string AgentName)[] participants)
    {
        var assembly = typeof(AIModule).Assembly;
        var participantType = assembly.GetType(
            "DigitalBrain.AI.OrchestrationParticipant",
            throwOnError: true)!;
        var definitionType = assembly.GetType(
            "DigitalBrain.AI.OrchestrationDefinition",
            throwOnError: true)!;
        var participantArray = Array.CreateInstance(participantType, participants.Length);

        for (var index = 0; index < participants.Length; index++)
        {
            var participant = participants[index];
            participantArray.SetValue(
                Activator.CreateInstance(
                    participantType,
                    participant.Contract,
                    participant.NeuronId,
                    participant.AgentId,
                    participant.AgentName),
                index);
        }

        var create = definitionType.GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(definitionType.FullName, "Create");
        var identityType = assembly.GetType(
            "DigitalBrain.AI.DirectOrchestrationIdentity",
            throwOnError: true)!;
        var identity = Activator.CreateInstance(
            identityType,
            Enum.Parse(
                assembly.GetType("DigitalBrain.AI.DirectOrchestrationKind", throwOnError: true)!,
                kind == "concurrent" ? "Concurrent" : "GroupChat"),
            Enum.Parse(
                assembly.GetType("DigitalBrain.AI.DirectExecutionEnvironment", throwOnError: true)!,
                executionEnvironment == "in-process-concurrent" ? "Concurrent" : "Lockstep"),
            Enum.Parse(
                assembly.GetType("DigitalBrain.AI.DirectOrchestrationManager", throwOnError: true)!,
                manager == "none" ? "None" : "RoundRobin"),
            Enum.Parse(
                assembly.GetType("DigitalBrain.AI.DirectOrchestrationAggregator", throwOnError: true)!,
                aggregator == "none" ? "None" : "ConcurrentDefault"))!;

        return create.Invoke(
            null,
            [orchestrationType, mafVersion, identity, participantArray, "application-test/1"])!;
    }

    private static object DescribeShapeDefinition(
        string factoryName,
        Type orchestrationType,
        IReadOnlyList<Participant> participants)
    {
        var shapeType = typeof(AIModule).Assembly.GetType(
            "DigitalBrain.AI.DirectOrchestrationShape",
            throwOnError: true)!;
        var factory = shapeType.GetMethod(
            factoryName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(shapeType.FullName, factoryName);
        var shape = factory.Invoke(null, [orchestrationType, participants])
            ?? throw new InvalidOperationException($"{factoryName} returned null.");

        return shapeType.GetProperty(
                "Definition",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(shape)
            ?? throw new MissingMemberException(shapeType.FullName, "Definition");
    }

    private static Type CreateDynamicOrchestrationType(string informationalVersion)
    {
        var assemblyName = new AssemblyName("DigitalBrain.Dynamic.Orchestration")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);
        var attribute = new CustomAttributeBuilder(
            typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
            [informationalVersion]);
        assembly.SetCustomAttribute(attribute);
        var module = assembly.DefineDynamicModule("DigitalBrain.Dynamic.Orchestration.dll");

        return module
            .DefineType(
                "DigitalBrain.Dynamic.TestOrchestration",
                TypeAttributes.Public | TypeAttributes.Class)
            .CreateType()!;
    }

    private static string Fingerprint(object definition)
        => (string)(definition.GetType().GetProperty("Fingerprint")?.GetValue(definition)
            ?? throw new MissingMemberException(definition.GetType().FullName, "Fingerprint"));

    private static object WithDefinitionValue(object definition, string property, object value)
    {
        var type = definition.GetType();
        string[] properties =
        [
            "FormatVersion",
            "MafVersion",
            "Fingerprint",
            "Participants",
            "HostId",
            "HostName",
            "Kind",
            "OrchestrationType",
            "ExecutionEnvironment",
            "Manager",
            "Aggregator",
            "ApplicationVersion",
        ];
        var index = Array.IndexOf(properties, property);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown definition property.");
        }

        var arguments = properties
            .Select(name => Property<object>(definition, name))
            .ToArray();
        arguments[index] = value;
        var constructor = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == properties.Length);

        return constructor.Invoke(arguments);
    }

    private static T Property<T>(object instance, string name)
        => (T)(instance.GetType().GetProperty(name)?.GetValue(instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, name));

    private static void RequireDefinitionMatch(object stored, object current)
    {
        var method = stored.GetType().GetMethod(
            "RequireMatch",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(stored.GetType().FullName, "RequireMatch");

        try
        {
            _ = method.Invoke(null, [stored, current]);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            throw failure.InnerException;
        }
    }

    [Fact(DisplayName = "orchestrations reject participant contracts that are not typed ILLM or IAgent capabilities")]
    public async Task InvalidParticipantContractsAreRejected()
    {
        using var llama = new OrchestrationChatClient("unused-llama");
        using var gpt = new OrchestrationChatClient("unused-gpt");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("invalid-participant");
            var target = NeuronId.For<IInvalidConcurrent>(owner, "invalid");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                probe.CallAsync(target, [new ChatMessage(ChatRole.User, "do not run")]));

            Assert.Contains(nameof(ILLM), failure.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(IAgent), failure.Message, StringComparison.Ordinal);
            Assert.Empty(llama.Calls);
            Assert.Empty(gpt.Calls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat resumes its one protected MAF session after deactivation")]
    public async Task GroupChatResumesProtectedSessionAfterDeactivation()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = GroupGptClient();
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("group-resume");
            var groupId = NeuronId.For<ITestGroupChat>(owner, "council");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            var first = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "first-question")]);
            var protectedState = await probe.ReadGroupStateAsync(groupId);
            var firstActivation = await probe.GroupActivationAsync(groupId);
            var reconciliation = gpt.Calls[1];
            var envelopeText = System.Text.Encoding.UTF8.GetString(protectedState);

            Assert.Contains("gpt-reconciled", first.Text, StringComparison.Ordinal);
            Assert.Contains(reconciliation, message => message.Text == "llama-independent");
            Assert.Contains(reconciliation, message => message.Text == "gpt-independent");
            Assert.NotEmpty(protectedState);
            Assert.DoesNotContain("first-question", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-reconciled", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("llama-independent", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-independent", envelopeText, StringComparison.Ordinal);
            using (var envelope = System.Text.Json.JsonDocument.Parse(protectedState))
            {
                Assert.Equal(
                    ["Definition", "EnvelopeVersion", "ProtectedSession"],
                    envelope.RootElement.EnumerateObject()
                        .Select(property => property.Name)
                        .Order(StringComparer.Ordinal));
            }

            await probe.DeactivateGroupAsync(groupId);

            var second = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "second-question")]);
            var secondActivation = await probe.GroupActivationAsync(groupId);
            var resumedState = await probe.ReadGroupStateAsync(groupId);
            Assert.Equal(2, llama.Calls.Count);
            Assert.Equal(4, gpt.Calls.Count);
            var secondReconciliation = gpt.Calls[3];

            Assert.Contains("gpt-reconciled", second.Text, StringComparison.Ordinal);
            Assert.NotEqual(firstActivation, secondActivation);
            Assert.NotEqual(protectedState, resumedState);
            Assert.Contains(llama.Calls[1], message => message.Text == "first-question");
            Assert.Contains(llama.Calls[1], message => message.Text == "second-question");
            Assert.Contains(secondReconciliation, message => message.Text == "first-question");
            Assert.Contains(secondReconciliation, message => message.Text == "second-question");
            Assert.Contains(secondReconciliation, message => message.Text == "llama-independent");
            Assert.Contains(secondReconciliation, message => message.Text == "gpt-independent");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat rejects composition drift before session access or participant calls")]
    public async Task GroupChatRejectsCompositionDriftWithoutMutation()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = GroupGptClient();
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("group-drift");
            var groupId = NeuronId.For<ITestGroupChat>(owner, "council");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            await probe.CallAsync(groupId, [new ChatMessage(ChatRole.User, "establish-session")]);
            var before = await probe.ReadGroupStateAsync(groupId);
            await probe.ChangeGroupParticipantAsync(groupId, "changed-participant");
            await probe.DeactivateGroupAsync(groupId);
            var carried = await probe.ReadGroupStateAsync(groupId);

            Assert.Equal(before, carried);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "must-not-run")]));
            var after = await probe.ReadGroupStateAsync(groupId);

            Assert.Contains("migration", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reset", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, after);
            Assert.Single(llama.Calls);
            Assert.Equal(2, gpt.Calls.Count);

            await probe.ChangeGroupParticipantAsync(groupId, name: null);
            await probe.DeactivateGroupAsync(groupId);

            var resumed = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "compatible-again")]);

            Assert.Contains("gpt-reconciled", resumed.Text, StringComparison.Ordinal);
            Assert.Equal(2, llama.Calls.Count);
            Assert.Equal(4, gpt.Calls.Count);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(
        OrchestrationChatClient llama,
        OrchestrationChatClient gpt,
        IJournalStorageProvider? journals = null)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(new byte[32]);
            silo.AddDigitalBrain("ai-orchestration-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton(journals ?? new VolatileJournalStorageProvider());
            silo.Services.AddKeyedSingleton<IChatClient>(typeof(Llama32), llama);
            silo.Services.AddKeyedSingleton<IChatClient>(typeof(Gpt56), gpt);
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return cluster;
    }

    private static OrchestrationChatClient GroupGptClient()
        => new(messages => messages.Any(message => message.Text == "llama-independent")
            && messages.Any(message => message.Text == "gpt-independent")
                ? "gpt-reconciled"
                : "gpt-independent");

    private static void AssertCapabilityRequest(
        SynapseDelivery request,
        SynapseDelivery cause,
        NeuronId caller,
        NeuronId target,
        JournalRead targetIncoming)
    {
        var capability = Assert.IsType<CapabilityRequested>(request.Synapse);
        var received = Assert.Single(
            targetIncoming.Delta,
            delivery => delivery.SynapseId == request.SynapseId);

        Assert.Equal(caller, request.Caller);
        Assert.Equal(target, capability.Target);
        Assert.Equal(typeof(ILLM).FullName, capability.Contract);
        Assert.Equal(nameof(ILLM.RespondAsync), capability.Method);
        Assert.Equal(cause.SynapseId, request.CausationId);
        Assert.Equal(cause.CorrelationId, request.CorrelationId);
        Assert.Equal(request.SynapseId, received.SynapseId);
        Assert.Equal(request.CorrelationId, received.CorrelationId);
        Assert.Equal(request.CausationId, received.CausationId);
        Assert.Equal(request.Caller, received.Caller);
        Assert.Equal(request.Sequence, received.Sequence);
        Assert.Equal(request.Timestamp, received.Timestamp);

        var receivedCapability = Assert.IsType<CapabilityRequested>(received.Synapse);
        Assert.Equal(capability.Target, receivedCapability.Target);
        Assert.Equal(capability.Contract, receivedCapability.Contract);
        Assert.Equal(capability.Method, receivedCapability.Method);
    }
}

[Alias("db.test.ai-orchestration-probe")]
[ClientEntryPoint]
internal interface IAIOrchestrationProbe : INeuron
{
    [Alias("Call")]
    Task<ChatResponse> CallAsync(NeuronId target, IReadOnlyList<ChatMessage> messages);

    [Alias("ReadGroupState")]
    Task<byte[]> ReadGroupStateAsync(NeuronId target);

    [Alias("ReadConcurrentState")]
    Task<byte[]> ReadConcurrentStateAsync(NeuronId target);

    [Alias("DeactivateConcurrent")]
    Task DeactivateConcurrentAsync(NeuronId target);

    [Alias("DeactivateGroup")]
    Task DeactivateGroupAsync(NeuronId target);

    [Alias("ChangeGroupParticipant")]
    Task ChangeGroupParticipantAsync(NeuronId target, string? name);

    [Alias("GroupActivation")]
    Task<Guid> GroupActivationAsync(NeuronId target);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(NeuronId target, JournalKind kind);
}

internal sealed class AIOrchestrationProbe : Neuron, IAIOrchestrationProbe
{
    public Task<ChatResponse> CallAsync(NeuronId target, IReadOnlyList<ChatMessage> messages)
        => GrainFactory.GetGrain<IAgent>(target.ToGrainId()).RespondAsync(messages);

    public Task<byte[]> ReadGroupStateAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ReadSessionStateAsync();

    public Task<byte[]> ReadConcurrentStateAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestConcurrent>(target.ToGrainId()).ReadSessionStateAsync();

    public Task DeactivateConcurrentAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestConcurrent>(target.ToGrainId()).DeactivateAsync();

    public Task DeactivateGroupAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).DeactivateAsync();

    public Task ChangeGroupParticipantAsync(NeuronId target, string? name)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ChangeParticipantAsync(name);

    public Task<Guid> GroupActivationAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ActivationAsync();

    public Task<JournalRead> ReadJournalAsync(NeuronId target, JournalKind kind)
        => GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadJournalAsync(kind, afterSequence: 0);
}

[Alias("db.test.concurrent")]
internal interface ITestConcurrent : IAgent
{
    [Alias("ReadSessionState")]
    Task<byte[]> ReadSessionStateAsync();

    [Alias("Deactivate")]
    Task DeactivateAsync();
}

internal sealed class TestConcurrent : Concurrent, ITestConcurrent
{
    private const string SessionStateName = "ai.concurrent.session";

    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>(),
        Participant<IGpt56>()
    ];

    public Task<byte[]> ReadSessionStateAsync()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(SessionStateName);

        return Task.FromResult(state.Value?.ToArray() ?? []);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }
}

[Alias("db.test.foreign-owner-concurrent")]
internal interface IForeignOwnerConcurrent : ITestConcurrent;

internal sealed class ForeignOwnerConcurrent : Concurrent, IForeignOwnerConcurrent
{
    private const string SessionStateName = "ai.concurrent.session";

    protected override IReadOnlyList<Participant> Participants =>
    [
        new Participant<ILlama32>(
            NeuronId.For<ILlama32>(new OwnerId("foreign-owner"), "model"))
    ];

    public Task<byte[]> ReadSessionStateAsync()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(SessionStateName);

        return Task.FromResult(state.Value?.ToArray() ?? []);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }
}

[Alias("db.test.invalid-concurrent")]
internal interface IInvalidConcurrent : IAgent;

internal sealed class InvalidConcurrent : Concurrent, IInvalidConcurrent
{
    protected override IReadOnlyList<Participant> Participants => [Participant<INeuron>()];
}

[Alias("db.test.group-chat")]
internal interface ITestGroupChat : IGroupChat
{
    [Alias("ReadSessionState")]
    Task<byte[]> ReadSessionStateAsync();

    [Alias("Deactivate")]
    Task DeactivateAsync();

    [Alias("ChangeParticipant")]
    Task ChangeParticipantAsync(string? name);

    [Alias("Activation")]
    Task<Guid> ActivationAsync();
}

internal sealed class TestGroupChat : GroupChat, ITestGroupChat
{
    private const string SessionStateName = "ai.group-chat.session";
    private readonly Guid _activation = Guid.NewGuid();

    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ITestConcurrent>(GroupDefinitionSource.NameFor(Id.Owner)),
        Participant<IGpt56>()
    ];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => throw new NotSupportedException();

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => throw new NotSupportedException();

    public Task<byte[]> ReadSessionStateAsync()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(SessionStateName);

        return Task.FromResult(state.Value?.ToArray() ?? []);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public Task ChangeParticipantAsync(string? name)
    {
        GroupDefinitionSource.Set(Id.Owner, name);

        return Task.CompletedTask;
    }

    public Task<Guid> ActivationAsync() => Task.FromResult(_activation);
}

[Alias("db.test.foreign-owner-group-chat")]
internal interface IForeignOwnerGroupChat : ITestGroupChat;

internal sealed class ForeignOwnerGroupChat : GroupChat, IForeignOwnerGroupChat
{
    private const string SessionStateName = "ai.group-chat.session";
    private readonly Guid _activation = Guid.NewGuid();

    protected override IReadOnlyList<Participant> Participants =>
    [
        new Participant<ILlama32>(
            NeuronId.For<ILlama32>(new OwnerId("foreign-owner"), "model"))
    ];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => throw new NotSupportedException();

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => throw new NotSupportedException();

    public Task<byte[]> ReadSessionStateAsync()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(SessionStateName);

        return Task.FromResult(state.Value?.ToArray() ?? []);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public Task ChangeParticipantAsync(string? name) => Task.CompletedTask;

    public Task<Guid> ActivationAsync() => Task.FromResult(_activation);
}

internal static class GroupDefinitionSource
{
    private static readonly ConcurrentDictionary<OwnerId, string> Names = new();

    internal static string? NameFor(OwnerId owner)
        => Names.GetValueOrDefault(owner);

    internal static void Set(OwnerId owner, string? name)
    {
        if (name is null)
        {
            Names.TryRemove(owner, out _);

            return;
        }

        Names[owner] = name;
    }
}

internal sealed class OrchestrationChatClient : IChatClient
{
    private readonly ConcurrentQueue<IReadOnlyList<ChatMessage>> _calls = new();
    private readonly Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> _answer;

    internal OrchestrationChatClient(string answer)
        : this((_, _) => Task.FromResult(answer))
    {
    }

    internal OrchestrationChatClient(Func<IReadOnlyList<ChatMessage>, string> answer)
        : this((messages, _) => Task.FromResult(answer(messages)))
    {
    }

    internal OrchestrationChatClient(
        Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<string>> answer)
    {
        _answer = answer;
    }

    internal IReadOnlyList<IReadOnlyList<ChatMessage>> Calls => [.. _calls];

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToArray();
        _calls.Enqueue(request);

        var answer = await _answer(request, cancellationToken);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

internal sealed class ConcurrentInvocationGate
{
    private readonly TaskCompletionSource _bothEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _entered;

    internal Task BothEntered => _bothEntered.Task;

    internal int Entered => Volatile.Read(ref _entered);

    internal async Task<string> EnterAsync(string answer, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _entered) == 2)
        {
            _bothEntered.TrySetResult();
        }

        await _release.Task.WaitAsync(cancellationToken);

        return answer;
    }

    internal void Release() => _release.TrySetResult();
}
