using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using DigitalBrain.AI;
using IntoChat.Tests.E2E.Agent;
using Npgsql;

namespace IntoChat.Tests.E2E.Diagnostics;

/// <summary>
/// Proves the trace budget from real exported spans, not from source inspection: an idle shell
/// stays quiet, the J1 intent stays within budget, a grain call is exactly two spans (one per
/// end), and the Orleans activity source is registered once (a duplicate doubles every call).
/// </summary>
public sealed class TraceBudgetFacts
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string OrleansSource = "Microsoft.Orleans.Application";

    [Fact(Timeout = 420_000)]
    public async Task IdleShellAndJ1StayWithinTheSpanBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint))
            .WithResourceEnvironment(OtlpEnvironment(collector))
            .StartAsync(ct);

        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        var idleStart = collector.Snapshot().Count;
        await Task.Delay(TimeSpan.FromSeconds(60), ct);
        var idleSpans = collector.Snapshot().Skip(idleStart).ToArray();
        Assert.True(idleSpans.Length < 20, $"Idle shell exported {idleSpans.Length} spans/min (budget < 20). Spans: {Describe(idleSpans)}");

        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var seed = new NpgsqlCommand("CREATE TABLE leads (id int, company text, email text, active boolean); INSERT INTO leads VALUES (1, 'Real company', 'real@example.test', true)", connection);
        await seed.ExecuteNonQueryAsync(ct);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        var intentStart = collector.Snapshot().Count;
        var input = new { workspaceId = "trace", threadId = "thread", runId = "run", messages = new[] { new { role = "user", content = "Show active leads" } } };
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent", input, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        var intentSpans = collector.Snapshot().Skip(intentStart).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine($"Idle {idleSpans.Length} spans/min; J1 {intentSpans.Length} spans total.\n{DescribeTree(intentSpans)}");
        // Plan P0.4 (docs/superpowers/plans/2026-09-23-intochat-product-delivery.md:248) and plan
        // P0.5 keep the whole-intent ceiling at <= 25 spans; P0.5 does not raise or split it.
        // Metering accumulates every provider call into the endpoint-scoped intent batch and
        // flushes one durable IIntentUsage/RecordBatchAsync write (2 spans) per completed intent.
        // That cost is paid by removing the query-window journal's later Complete round-trip
        // (2 spans): Begin still records the pre-work fingerprint, and the workspace Open receipt
        // is now the durable replay source carrying the applied revision. The conversation Read
        // plus its revision-conflict guard stays; net P0.5 cost is zero spans, so the P0.4
        // measurement of 25 stands.
        const int J1SpanBudget = 25;
        Assert.True(intentSpans.Length <= J1SpanBudget, $"J1 exported {intentSpans.Length} spans in total (budget <= {J1SpanBudget}).\n{DescribeTree(intentSpans)}");
        Assert.Contains(intentSpans, IsGenAiSpan);
        Assert.Contains(intentSpans, span => string.Equals(span.Scope, "Npgsql", StringComparison.Ordinal));
        Assert.Contains(intentSpans, span => string.Equals(span.Name, "IIntentUsage/RecordBatchAsync", StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task OneGrainCallEmitsExactlyTwoSpansFromASingleOrleansSource()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(OtlpEnvironment(collector))
            .StartAsync(ct);

        // The conversation read endpoint drives exactly one silo-internal grain call. Warm the
        // activation first so its one-time spans are not counted as part of the measured call.
        using (var warm = await brain.HttpClient.GetAsync(ConversationPath, ct))
        {
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        var before = collector.Snapshot().Count;
        using (var response = await brain.HttpClient.GetAsync(ConversationPath, ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await WaitForAsync(() => GrainCallSpans(collector, before).Length >= 2, TimeSpan.FromSeconds(15), ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        var callSpans = GrainCallSpans(collector, before);

        // One grain call crosses the wire once: a client span and a server span, never two of each.
        Assert.True(callSpans.Length == 2, $"A grain call exported {callSpans.Length} spans: {Describe(callSpans)}");
        Assert.Equal([2, 3], callSpans.Select(span => span.Kind).Order().ToArray());
        Assert.Equal([OrleansSource], callSpans.Select(span => span.Scope).Distinct(StringComparer.Ordinal).ToArray());
        Assert.Empty(collector.Errors());
    }

    private const string ConversationPath = "/workspaces/trace/conversations/thread";

    private static CapturedSpan[] GrainCallSpans(TestTelemetryCollector collector, int from)
        => collector.Snapshot().Skip(from)
            .Where(span => string.Equals(span.Scope, OrleansSource, StringComparison.Ordinal)
                && string.Equals(span.Name, "IAgent/ReadConversation", StringComparison.Ordinal))
            .ToArray();

    private static Dictionary<string, string> OtlpEnvironment(TestTelemetryCollector collector) => new(StringComparer.Ordinal)
    {
        [OtlpEndpointKey] = collector.Endpoint.AbsoluteUri,
        ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
    };

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private static string Describe(IEnumerable<CapturedSpan> spans)
        => string.Join("; ", spans.Select(span => $"{span.Scope}:{span.Name}[kind={span.Kind}]"));

    private static bool IsGenAiSpan(CapturedSpan span)
        => span.Scope.StartsWith("DigitalBrain.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal);

    // Renders the intent's spans as a parent/child tree so a budget failure shows where the
    // spans come from, not just a flat count.
    private static string DescribeTree(IReadOnlyList<CapturedSpan> spans)
    {
        var byParent = spans.ToLookup(span => span.ParentSpanId ?? string.Empty, StringComparer.Ordinal);
        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var lines = new List<string>();
        void Walk(CapturedSpan span, int depth)
        {
            lines.Add($"{new string(' ', depth * 2)}{span.Scope}:{span.Name}[kind={span.Kind}]");
            foreach (var child in byParent[span.SpanId]) { Walk(child, depth + 1); }
        }

        foreach (var root in spans.Where(span => span.ParentSpanId is null || !ids.Contains(span.ParentSpanId)))
        {
            Walk(root, 0);
        }

        var total = spans.Count;
        var perScope = string.Join(", ", spans.GroupBy(span => span.Scope, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}={group.Count()}"));
        return $"J1 span tree ({total} total; {perScope}):\n" + string.Join("\n", lines);
    }
}