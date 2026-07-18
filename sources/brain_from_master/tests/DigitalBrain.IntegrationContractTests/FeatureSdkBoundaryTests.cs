using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DigitalBrain.Features.EmailSummarizer;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Features.Testing;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class FeatureSdkBoundaryTests
{
    [Fact]
    public void Feature_contract_has_one_async_entry_point()
    {
        var method = Assert.Single(typeof(IFeature).GetMethods());
        Assert.Equal("HandleAsync", method.Name);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(
            [typeof(FeatureInput), typeof(IFeatureContext), typeof(CancellationToken)],
            parameters.Select(static parameter => parameter.ParameterType));
        Assert.True(parameters[^1].IsOptional);
    }

    [Fact]
    public void Feature_context_exposes_every_bounded_runtime_port()
    {
        var properties = typeof(IFeatureContext)
            .GetProperties()
            .Select(static property => (property.Name, property.PropertyType))
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                ("Clock", typeof(IFeatureClock)),
                ("Identifiers", typeof(IFeatureIdentifiers)),
                ("Intents", typeof(IFeatureIntentBuffer)),
                ("MemoryRecall", typeof(IMemoryRecall)),
                ("MemoryRemember", typeof(IMemoryRemember)),
                ("Models", typeof(IModelWorkflow)),
                ("State", typeof(IFeatureState))
            ],
            properties);
        Assert.DoesNotContain(
            typeof(IFeatureContext).GetMethods(),
            static method => !method.IsSpecialName);
    }

    [Fact]
    public void Feature_projects_have_exact_dependency_directions()
    {
        AssertProjectReferences(
            "src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj",
            []);
        AssertProjectReferences(
            "src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj",
            [
                "src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj",
                "integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj"
            ]);
        AssertProjectReferences(
            "features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj",
            [
                "src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj",
                "integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj"
            ]);
    }

    [Fact]
    public void Feature_projects_have_no_unapproved_package_dependencies()
    {
        AssertPackageReferences("src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj", []);
        AssertPackageReferences("src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj", ["Reqnroll"]);
        AssertPackageReferences("features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj", []);
    }

    [Fact]
    public void Email_summarizer_uses_strict_Reqnroll_configuration_and_shared_bindings()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "features",
            "EmailSummarizer.Tests",
            "reqnroll.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var runtime = root.GetProperty("runtime");

        Assert.Equal("Error", runtime.GetProperty("missingOrPendingStepsOutcome").GetString());
        Assert.False(runtime.GetProperty("stopAtFirstError").GetBoolean());
        var binding = Assert.Single(root.GetProperty("bindingAssemblies").EnumerateArray());
        Assert.Equal("DigitalBrain.Features.Testing", binding.GetProperty("assembly").GetString());
    }

    [Fact]
    public void Feature_assemblies_have_no_static_mutable_state()
    {
        Assembly[] assemblies =
        [
            typeof(IFeature).Assembly,
            typeof(FeatureScenarioContext).Assembly,
            typeof(EmailSummarizerFeature).Assembly
        ];
        var offenders = assemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .SelectMany(static type => type.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly))
            .Where(static field => !field.IsLiteral && !field.IsInitOnly)
            .Select(static field => $"{field.DeclaringType!.FullName}.{field.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Feature_assembly_references_match_the_public_allowlist()
    {
        var expected = new Dictionary<Assembly, string[]>
        {
            [typeof(IFeature).Assembly] = ["System.Collections", "System.Runtime", "System.Text.Json"],
            [typeof(FeatureScenarioContext).Assembly] =
            [
                "DigitalBrain.Features.Sdk",
                "DigitalBrain.Integrations.Google.Contracts",
                "Reqnroll",
                "System.Collections",
                "System.Runtime",
                "System.Threading"
            ],
            [typeof(EmailSummarizerFeature).Assembly] =
            [
                "DigitalBrain.Features.Sdk",
                "DigitalBrain.Integrations.Google.Contracts",
                "System.Runtime"
            ]
        };

        foreach (var pair in expected)
        {
            var actual = pair.Key
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(pair.Value.Order(StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void Email_summarizer_requires_its_Gmail_reader()
    {
        Assert.Throws<ArgumentNullException>(() => new EmailSummarizerFeature(null!));
    }

    [Fact]
    public async Task Scenario_execution_propagates_cancellation()
    {
        var scenario = new FeatureScenarioContext();
        var input = new FeatureInput(
            "input-cancelled",
            "test.input.v1",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scenario.ExecuteAsync(new CancellationFeature(), input, cancellation.Token));
    }

    [Fact]
    public async Task Scenario_intent_buffer_rejects_the_thirty_third_intent()
    {
        var scenario = new FeatureScenarioContext();
        var result = await scenario.ExecuteAsync(
            new TooManyIntentsFeature(),
            TestInput("input-too-many-intents"));

        Assert.Equal(FeatureExecutionStatus.Failed, result.Status);
        Assert.Equal("A Feature run cannot buffer more than 32 intents.", result.Message);
        Assert.Empty(scenario.Surfaces);
    }

    [Fact]
    public async Task Scenario_execution_discards_intents_when_the_feature_fails()
    {
        var scenario = new FeatureScenarioContext();
        var input = TestInput("input-failed");

        var result = await scenario.ExecuteAsync(new EmitThenFailFeature(), input);

        Assert.Equal(FeatureExecutionStatus.Failed, result.Status);
        Assert.Empty(scenario.Surfaces);
    }

    [Fact]
    public async Task Scenario_execution_claims_concurrent_duplicate_input_once()
    {
        var scenario = new FeatureScenarioContext();
        var feature = new BlockingFeature();
        var input = TestInput("input-concurrent");

        var first = scenario.ExecuteAsync(feature, input);
        await feature.Started;
        var second = scenario.ExecuteAsync(feature, input);
        feature.Release();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, feature.ExecutionCount);
        Assert.Single(results, static result => !result.Duplicate);
        Assert.Single(results, static result => result.Duplicate);
        Assert.Single(scenario.Surfaces);
    }

    [Fact]
    public async Task Scenario_model_fake_misses_when_the_request_is_not_exact()
    {
        var scenario = new FeatureScenarioContext();
        scenario.ConfigureModelResponse(
            new ModelRequest("workflow", "expected prompt", "expected-key"),
            "configured response");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Models.CompleteAsync(new ModelRequest("workflow", "wrong prompt", "wrong-key")));

        Assert.Equal("No model response configured for workflow.", exception.Message);
    }

    [Fact]
    public void Scenario_time_and_identifiers_are_deterministic_across_resets()
    {
        var scenario = new FeatureScenarioContext();

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), scenario.Clock.UtcNow);
        Assert.Equal("intent-00000001", scenario.Identifiers.Next("intent"));
        Assert.Equal("intent-00000002", scenario.Identifiers.Next("intent"));

        scenario.SetTime(DateTimeOffset.UnixEpoch);
        scenario.Reset();

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), scenario.Clock.UtcNow);
        Assert.Equal("intent-00000001", scenario.Identifiers.Next("intent"));
    }

    [Fact]
    public async Task Scenario_commits_every_intent_category_only_after_success()
    {
        var scenario = new FeatureScenarioContext();

        var result = await scenario.ExecuteAsync(
            new EveryIntentFeature(),
            TestInput("input-every-intent"));

        Assert.Equal(FeatureExecutionStatus.Succeeded, result.Status);
        Assert.Equal("{\"complete\":true}", scenario.State.Read().Json);
        Assert.Single(scenario.Surfaces);
        Assert.Single(scenario.Events);
        Assert.Single(scenario.ExternalEffects);
        Assert.Single(scenario.MemoryWrites);
    }

    [Fact]
    public async Task Scenario_serializes_different_inputs_and_preserves_state_updates()
    {
        var scenario = new FeatureScenarioContext();
        var feature = new SequencedStateFeature();

        var first = scenario.ExecuteAsync(feature, TestInput("input-state-1"));
        await feature.FirstStarted;
        var second = scenario.ExecuteAsync(feature, TestInput("input-state-2"));
        await Task.Delay(25);

        Assert.Equal(1, feature.ExecutionCount);

        feature.ReleaseFirst();
        await Task.WhenAll(first, second);

        Assert.Equal(2, feature.ExecutionCount);
        Assert.Equal("{\"value\":2}", scenario.State.Read().Json);
    }

    [Fact]
    public async Task Scenario_follower_retries_after_the_leader_is_cancelled()
    {
        var scenario = new FeatureScenarioContext();
        var feature = new CancelFirstFeature();
        var input = TestInput("input-leader-cancelled");
        using var cancellation = new CancellationTokenSource();

        var leader = scenario.ExecuteAsync(feature, input, cancellation.Token);
        await feature.FirstStarted;
        var follower = scenario.ExecuteAsync(feature, input);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);
        var result = await follower;

        Assert.Equal(FeatureExecutionStatus.Succeeded, result.Status);
        Assert.False(result.Duplicate);
        Assert.Equal(2, feature.ExecutionCount);
        Assert.Single(scenario.Surfaces);
    }

    [Fact]
    public async Task Scenario_rejects_the_twenty_first_capability_read()
    {
        var scenario = new FeatureScenarioContext();
        scenario.ConfigureMessage(new DigitalBrain.Integrations.Google.Contracts.GmailMessage(
            "message-budget",
            null,
            DateTimeOffset.UnixEpoch,
            "sender@example.com",
            "Subject",
            "Body"));
        scenario.SetGmailReadGrant(true);

        var result = await scenario.ExecuteAsync(
            new CapabilityReadBudgetFeature(scenario.GmailReader),
            TestInput("input-read-budget"));

        Assert.Equal(FeatureExecutionStatus.Failed, result.Status);
        Assert.Equal("A Feature run cannot perform more than 20 capability reads.", result.Message);
        Assert.Equal(20, scenario.GmailReadCount);
    }

    [Fact]
    public async Task Scenario_rejects_the_fifth_model_call()
    {
        var scenario = new FeatureScenarioContext();
        var request = new ModelRequest("workflow", "prompt", "operation");
        scenario.ConfigureModelResponse(request, "response");

        var result = await scenario.ExecuteAsync(
            new ModelBudgetFeature(request),
            TestInput("input-model-budget"));

        Assert.Equal(FeatureExecutionStatus.Failed, result.Status);
        Assert.Equal("A Feature run cannot perform more than 4 model calls.", result.Message);
        Assert.Equal(4, scenario.ModelCallCount);
    }

    [Fact]
    public void Feature_json_and_text_limits_use_utf8_bytes()
    {
        Assert.Throws<ArgumentException>(() => new FeatureState("not-json"));
        Assert.Throws<ArgumentException>(() =>
            new FeatureState($"\"{new string('\u20ac', 21_846)}\""));
        Assert.Throws<ArgumentException>(() =>
            new EventIntent("operation", "test.event.v1", "not-json"));
        Assert.Throws<ArgumentException>(() =>
            new ExternalEffectIntent("operation", "test.effect.v1", null, "not-json"));
        Assert.Throws<ArgumentException>(() =>
            new MemoryFact("fact", new string('\u20ac', 683), [], DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() =>
            new MemoryRememberIntent("operation", "fact", new string('\u20ac', 683), []));
    }

    [Fact]
    public async Task Scenario_executes_different_inputs_in_arrival_order()
    {
        var scenario = new FeatureScenarioContext();
        var feature = new FifoFeature();

        var first = scenario.ExecuteAsync(feature, TestInput("input-fifo-1"));
        await feature.FirstStarted;
        var second = scenario.ExecuteAsync(feature, TestInput("input-fifo-2"));
        var third = scenario.ExecuteAsync(feature, TestInput("input-fifo-3"));
        feature.ReleaseFirst();
        await Task.WhenAll(first, second, third);

        Assert.Equal(["input-fifo-1", "input-fifo-2", "input-fifo-3"], feature.ExecutionOrder);
    }

    [Fact]
    public async Task Scenario_queued_cancellation_does_not_release_the_next_input_early()
    {
        var scenario = new FeatureScenarioContext();
        var feature = new QueuedCancellationFeature();
        using var cancellation = new CancellationTokenSource();

        var first = scenario.ExecuteAsync(feature, TestInput("input-queue-a"));
        await feature.FirstStarted;
        var second = scenario.ExecuteAsync(feature, TestInput("input-queue-b"), cancellation.Token);
        var third = scenario.ExecuteAsync(feature, TestInput("input-queue-c"));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        using (var waitCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                feature.ThirdStarted.WaitAsync(waitCancellation.Token));
        }

        feature.ReleaseFirst();
        await Task.WhenAll(first, third);

        Assert.Equal(["input-queue-a", "input-queue-c"], feature.ExecutionOrder);
    }

    [Fact]
    public async Task Scenario_enforces_model_budget_across_parallel_calls()
    {
        var scenario = new FeatureScenarioContext();
        var request = new ModelRequest("parallel-workflow", "prompt", "operation");
        scenario.ConfigureModelResponse(request, "response");

        var result = await scenario.ExecuteAsync(
            new ParallelModelBudgetFeature(request),
            TestInput("input-parallel-model-budget"));

        Assert.Equal(FeatureExecutionStatus.Failed, result.Status);
        Assert.Equal("A Feature run cannot perform more than 4 model calls.", result.Message);
        Assert.Equal(4, scenario.ModelCallCount);
    }

    [Fact]
    public async Task Email_summarizer_truncates_multibyte_body_to_the_model_budget()
    {
        var scenario = new FeatureScenarioContext();
        var body = new string('\u20ac', 20_000);
        var prefix = "Summarize this email.\nSubject: Subject\nBody: ";
        var remainingBytes = 32_768 - Encoding.UTF8.GetByteCount(prefix);
        var expectedBody = new string('\u20ac', remainingBytes / 3);
        scenario.ConfigureMessage(new DigitalBrain.Integrations.Google.Contracts.GmailMessage(
            "message-unicode",
            null,
            DateTimeOffset.UnixEpoch,
            "sender@example.com",
            "Subject",
            body));
        scenario.SetGmailReadGrant(true);
        scenario.ConfigureModelResponse(
            new ModelRequest("email-summary", prefix + expectedBody, "generate-summary"),
            "summary");
        var input = new FeatureInput(
            "input-unicode",
            "gmail.message.summary.requested.v1",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string> { ["messageId"] = "message-unicode" });

        var result = await scenario.ExecuteAsync(
            new EmailSummarizerFeature(scenario.GmailReader),
            input);

        Assert.Equal(FeatureExecutionStatus.Succeeded, result.Status);
        Assert.True(Encoding.UTF8.GetByteCount(Assert.Single(scenario.ModelRequests).Prompt) <= 32_768);
    }

    private static void AssertProjectReferences(string relativePath, string[] expected)
    {
        var root = RepositoryRoot();
        var projectPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var actual = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFullPath(
                Path.Combine(
                    projectDirectory,
                    reference.Attribute("Include")!.Value
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar))))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static void AssertPackageReferences(string relativePath, string[] expected)
    {
        var projectPath = Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var actual = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static FeatureInput TestInput(string inputId) =>
        new(
            inputId,
            "test.input.v1",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class CancellationFeature : IFeature
    {
        public Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled(cancellationToken);
    }

    private sealed class EmitThenFailFeature : IFeature
    {
        public Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            context.Intents.AddTextSurface(new TextSurfaceIntent(input.InputId, "Title", "Text"));
            throw new InvalidOperationException("failed after intent");
        }
    }

    private sealed class TooManyIntentsFeature : IFeature
    {
        public Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < 33; index++)
            {
                context.Intents.AddTextSurface(new TextSurfaceIntent(
                    $"operation-{index}",
                    "Title",
                    "Text"));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class EveryIntentFeature : IFeature
    {
        public Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            context.State.Replace(new FeatureState("{\"complete\":true}"));
            context.Intents.AddTextSurface(new TextSurfaceIntent(input.InputId, "Title", "Text"));
            context.Intents.EmitEvent(new EventIntent(input.InputId, "test.event.v1", "{}"));
            context.Intents.ProposeExternalEffect(new ExternalEffectIntent(
                input.InputId,
                "test.effect.propose.v1",
                "connection-1",
                "{}"));
            context.MemoryRemember.Remember(new MemoryRememberIntent(
                input.InputId,
                "fact-1",
                "Remembered text",
                ["test"]));
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedStateFeature : IFeature
    {
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        public Task FirstStarted => _firstStarted.Task;
        public int ExecutionCount => Volatile.Read(ref _executionCount);
        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            var execution = Interlocked.Increment(ref _executionCount);
            var current = context.State.Read().Json == "{}" ? 0 : 1;
            if (execution == 1)
            {
                _firstStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }

            context.State.Replace(new FeatureState($"{{\"value\":{current + 1}}}"));
        }
    }

    private sealed class CancelFirstFeature : IFeature
    {
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        public Task FirstStarted => _firstStarted.Task;
        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _executionCount) == 1)
            {
                _firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            context.Intents.AddTextSurface(new TextSurfaceIntent(input.InputId, "Title", "Text"));
        }
    }

    private sealed class CapabilityReadBudgetFeature(
        DigitalBrain.Integrations.Google.Contracts.IGmailMessageReader gmail) : IFeature
    {
        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < 21; index++)
            {
                await gmail.ReadAsync(
                    new DigitalBrain.Integrations.Google.Contracts.GmailMessageReadRequest("message-budget"),
                    cancellationToken);
            }
        }
    }

    private sealed class ModelBudgetFeature(ModelRequest request) : IFeature
    {
        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < 5; index++)
            {
                await context.Models.CompleteAsync(request, cancellationToken);
            }
        }
    }

    private sealed class FifoFeature : IFeature
    {
        private readonly object _gate = new();
        private readonly List<string> _executionOrder = [];
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstStarted => _firstStarted.Task;
        public IReadOnlyList<string> ExecutionOrder
        {
            get
            {
                lock (_gate)
                {
                    return _executionOrder.ToArray();
                }
            }
        }

        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            int position;
            lock (_gate)
            {
                _executionOrder.Add(input.InputId);
                position = _executionOrder.Count;
            }

            if (position == 1)
            {
                _firstStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class ParallelModelBudgetFeature(ModelRequest request) : IFeature
    {
        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            var ready = 0;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new Task[8];
            for (var index = 0; index < calls.Length; index++)
            {
                calls[index] = Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref ready) == calls.Length)
                    {
                        release.TrySetResult();
                    }

                    await release.Task;
                    await context.Models.CompleteAsync(request, cancellationToken);
                }, cancellationToken);
            }

            await Task.WhenAll(calls);
        }
    }

    private sealed class QueuedCancellationFeature : IFeature
    {
        private readonly object _gate = new();
        private readonly List<string> _executionOrder = [];
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _thirdStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstStarted => _firstStarted.Task;
        public Task ThirdStarted => _thirdStarted.Task;
        public IReadOnlyList<string> ExecutionOrder
        {
            get
            {
                lock (_gate)
                {
                    return _executionOrder.ToArray();
                }
            }
        }

        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _executionOrder.Add(input.InputId);
            }

            if (input.InputId == "input-queue-a")
            {
                _firstStarted.TrySetResult();
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (input.InputId == "input-queue-c")
            {
                _thirdStarted.TrySetResult();
            }
        }
    }

    private sealed class BlockingFeature : IFeature
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        public Task Started => _started.Task;
        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public void Release() => _release.TrySetResult();

        public async Task HandleAsync(
            FeatureInput input,
            IFeatureContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            context.Intents.AddTextSurface(new TextSurfaceIntent(input.InputId, "Title", "Text"));
        }
    }
}
