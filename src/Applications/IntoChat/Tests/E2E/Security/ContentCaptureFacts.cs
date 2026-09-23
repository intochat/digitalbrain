using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DigitalBrain.AI;
using DigitalBrain.Testing.E2E;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Diagnostics;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Security;

/// <summary>
/// D14 capture policy (plan P0.6): content capture stays on only for the local owner's
/// development runs, is off in a Production host and for any other principal, values tagged
/// Personal or Credential are never captured even in development, and GenAI token metadata
/// survives with capture off. These facts read the real exported OTLP GenAI spans and logs,
/// not source inspection.
/// </summary>
public sealed class ContentCaptureFacts
{
    private const string OrdinaryContent = "Show active leads";
    private const string PersonalCanary = "personal-canary-7f3a91";
    private const string EnvironmentKey = "ASPNETCORE_ENVIRONMENT";
    private const string EnableSensitiveKey = "DigitalBrain__AI__Telemetry__EnableSensitiveData";
    private const string CaptureMessageContentKey = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
    private const string ProfileKey = "IntoChat__Profile";

    [Fact(Timeout = 300_000)]
    public async Task LocalOwnerDevCapturesOrdinaryContentButNeverTaggedPersonalValues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, LocalOwnerEnvironment(), ct);
        await LeadData.SeedAsync(brain, "Capture", ct);

        await AskAsync(brain, "capture", "ordinary", "ordinary-run", OrdinaryContent, @class: null, ct);
        await WaitForAsync(() => collector.Snapshot().Any(span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal)), ct);

        // The goal of 739c6c385 is kept: the local owner's ordinary dev turn shows its content.
        Assert.Contains(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        // Capture does not cost the token/GenAI metadata the P0.5 metering and P0.4 spans require.
        Assert.Contains(collector.Snapshot(), span => span.Attributes.Keys.Any(key => key.StartsWith("gen_ai.usage", StringComparison.Ordinal)));

        // A value explicitly tagged Personal must never reach GenAI spans or logs, even in dev.
        await AskAsync(brain, "capture", "personal", "personal-run", $"My personal note is {PersonalCanary}", "Personal", ct);
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(PersonalCanary, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(PersonalCanary, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task HostedRunsNeverCaptureContent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, HostedEnvironment(), ct);
        await LeadData.SeedAsync(brain, "Capture", ct);

        await AskAsync(brain, "capture", "hosted", "hosted-run", OrdinaryContent, @class: null, ct);
        await WaitForAsync(() => collector.Snapshot().Any(IsGenAiSpan), ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        // The turn ran with full instrumentation; only the content was withheld.
        Assert.Contains(collector.Snapshot(), IsGenAiSpan);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task NonOwnerRunsNeverCaptureContent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, NonOwnerEnvironment(), ct);
        brain.HttpClient.DefaultRequestHeaders.Authorization = Basic("intruder", "intruder-secret");
        await LeadData.SeedAsync(brain, "Capture", ct);

        await AskAsync(brain, "capture", "intruder", "intruder-run", OrdinaryContent, @class: null, ct);
        await WaitForAsync(() => collector.Snapshot().Any(IsGenAiSpan), ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        Assert.Contains(collector.Snapshot(), IsGenAiSpan);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task ProductionDeveloperProfileNeverCapturesContentEvenWithSensitiveDataOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, ProductionDeveloperEnvironment(), ct);
        await LeadData.SeedAsync(brain, "Capture", ct);

        await AskAsync(brain, "capture", "production-developer", "production-developer-run", OrdinaryContent, @class: null, ct);
        await WaitForAsync(() => collector.Snapshot().Any(IsGenAiSpan), ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        // A Production host keeps GenAI/token metadata but never the content, even when the
        // explicit EnableSensitiveData setting is on and the profile is the developer one.
        Assert.Contains(collector.Snapshot(), span => span.Attributes.Keys.Any(key => key.StartsWith("gen_ai.usage", StringComparison.Ordinal)));
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task CaptureMessageContentVariableCannotOverrideExplicitSensitiveDataOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, AmbientCaptureVariableEnvironment(), ct);
        await LeadData.SeedAsync(brain, "Capture", ct);

        await AskAsync(brain, "capture", "ambient-variable", "ambient-variable-run", OrdinaryContent, @class: null, ct);
        await WaitForAsync(() => collector.Snapshot().Any(IsGenAiSpan), ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        // The standard OTEL capture variable must not turn capture on: the explicit
        // Telemetry:EnableSensitiveData=false setting wins even for the local owner in Development.
        Assert.Contains(collector.Snapshot(), IsGenAiSpan);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(OrdinaryContent, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    private static Dictionary<string, string> LocalOwnerEnvironment() => new(StringComparer.Ordinal)
    {
        [EnvironmentKey] = "Development",
        [ProfileKey] = "developer",
    };

    private static Dictionary<string, string> HostedEnvironment() => new(StringComparer.Ordinal)
    {
        [EnvironmentKey] = "Production",
        [ProfileKey] = "product",
    };

    private static Dictionary<string, string> NonOwnerEnvironment() => new(StringComparer.Ordinal)
    {
        [EnvironmentKey] = "Development",
        [ProfileKey] = "developer",
        ["DigitalBrain__Auth__Username"] = "intruder",
        ["DigitalBrain__Auth__Password"] = "intruder-secret",
    };

    private static Dictionary<string, string> ProductionDeveloperEnvironment() => new(StringComparer.Ordinal)
    {
        [EnvironmentKey] = "Production",
        [ProfileKey] = "developer",
    };

    private static Dictionary<string, string> AmbientCaptureVariableEnvironment() => new(StringComparer.Ordinal)
    {
        [EnvironmentKey] = "Development",
        [ProfileKey] = "developer",
        [EnableSensitiveKey] = "false",
        [CaptureMessageContentKey] = "true",
    };

    private static AuthenticationHeaderValue Basic(string username, string password)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

    private static async Task AskAsync(E2EBrain brain, string workspaceId, string threadId, string runId,
        string content, string? @class, CancellationToken ct)
    {
        // The class tag is the minimal Phase-0 sensitivity marker the endpoint reads for the turn.
        object message = @class is null
            ? new { role = "user", content }
            : new { role = "user", content, @class };
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent",
            new { workspaceId, threadId, runId, messages = new[] { message } }, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream);
    }

    private static bool IsGenAiSpan(CapturedSpan span)
        => span.Scope.StartsWith("DigitalBrain.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal);

    private static string TextOf(CapturedSpan span)
        => string.Join("\n", span.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    private static string TextOf(CapturedLog log)
        => log.Body + "\n" + string.Join("\n", log.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private static class CaptureTest
    {
        public static Task<E2EBrain> CreateAsync(ScriptedModelServer model, TestTelemetryCollector collector,
            IReadOnlyDictionary<string, string> environment, CancellationToken ct)
        {
            var primary = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                [EnableSensitiveKey] = "true",
            };
            foreach (var (key, value) in environment) { primary[key] = value; }
            return IntoChatE2ETest.Create()
                .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
                .WithResourceEnvironment(primary)
                .StartAsync(ct);
        }
    }
}