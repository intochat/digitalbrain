using static IntoChat.Tests.E2E.Diagnostics.TraceAssertions;
using System.Net.Http.Json;
using DigitalBrain.AI;
using DigitalBrain.Testing.E2E;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Diagnostics;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Security;

// The module option is the only content-capture switch. Check the exported spans/logs,
// including a Production host to catch accidental environment/profile gates.
public sealed class ContentCaptureFacts
{
    private const string OrdinaryContent = "Show active leads";
    private const string EnableSensitiveKey = "DigitalBrain__AI__Telemetry__EnableSensitiveData";

    [Theory(Timeout = 300_000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModuleSettingControlsExportedContent(bool enabled)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await CaptureTest.CreateAsync(model, collector, new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DigitalBrain__Testing__Enabled"] = "true",
            ["IntoChat__Profile"] = "developer",
            [EnableSensitiveKey] = enabled ? "true" : "false",
            // An ambient SDK setting cannot override the explicit module setting.
            ["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"] = "true",
        }, ct);
        await LeadData.SeedAsync(brain, "Capture", ct);
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent", new
        {
            brainId = "capture",
            threadId = "capture",
            runId = "capture-run",
            messages = new[] { new { role = "user", content = OrdinaryContent } },
        }, ct);
        Assert.Contains("RUN_FINISHED", await response.Content.ReadAsStringAsync(ct));
        await WaitForAsync(() => collector.Snapshot().Any(IsGenAiSpan), ct);
        if (enabled)
        {
            await WaitForAsync(() => collector.Snapshot().Any(span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal)), ct);
            Assert.Contains(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
        }
        else
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(OrdinaryContent, StringComparison.Ordinal));
            Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(OrdinaryContent, StringComparison.Ordinal));
        }
        Assert.Contains(collector.Snapshot(), IsGenAiSpan);
        Assert.Contains(collector.Snapshot(), span => span.Attributes.Keys.Any(key => key.StartsWith("gen_ai.usage", StringComparison.Ordinal)));
        Assert.Empty(collector.Errors());
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
                .ConfigureModule<AIModule, AIOptions>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
                .WithResourceEnvironment(primary)
                .StartAsync(ct);
        }
    }
}

