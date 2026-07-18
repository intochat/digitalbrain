using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting.Llm;
using Ino.Core.Hosting.ML;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Core.Hosting.Capabilities;

public sealed class CortexCapability(
    IDiscoveryClient discovery,
    IFirePort firePort,
    IChatClient chatClient,
    INeuronPromptCorpus corpus,
    IGrainFactory grainFactory,
    ILogger<CortexCapability> log) : ICortexCapability
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record FastPathHit(NeuronResult Outcome, string? ScenarioName);

    public async Task<RoutingResult> RouteAsync(string prompt, NeuronContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var neurons = await discovery.DumpNeuronsAsync(ct);
        if (neurons.Count == 0)
            return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Unrouted, ScenarioName: null);

        var features = BuildRoutingFeatures(prompt, neurons);

        if (await TryFastPathAsync(prompt, neurons, ctx, ct) is { } fast)
        {
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: true, ct,
                prompt: prompt, source: RoutingSource.Regex,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(fast.Outcome, RoutingSource.Regex, fast.ScenarioName);
        }

        // Skip the LLM classifier when the per-user optimizer is confident
        // this prompt won't route — the model has seen enough off-topic
        // prompts from this user to short-circuit a doomed gen_ai call.
        if (await PredictWillRouteAsync(ctx.UserId, features, ct) is OptimizationResult pred
            && pred is { Predicted: false, Confidence: >= 0.90f })
        {
            log.LogDebug(
                "Cortex: optimizer skipped LLM classifier for user {User} ({Confidence:F2} confident in unrouted)",
                ctx.UserId, pred.Confidence);
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: false, ct,
                prompt: prompt, source: RoutingSource.Ml,
                mlPrediction: pred.Predicted ? 1.0 : 0.0, mlConfidence: pred.Confidence,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Ml, ScenarioName: null);
        }

        if (await TryClassifyWithLlmAsync(prompt, neurons, ctx, ct) is { } llmResult)
        {
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: true, ct,
                prompt: prompt, source: RoutingSource.Llm, llmCalled: true,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(llmResult, RoutingSource.Llm, ScenarioName: null);
        }

        await RecordRoutingDecisionAsync(ctx.UserId, features, routed: false, ct,
            prompt: prompt, source: RoutingSource.Unrouted, llmCalled: true,
            correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
        return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Unrouted, ScenarioName: null);
    }

    // Per-user optimizer key. Stable across activations so the journal
    // keeps accumulating per-user history.
    static string OptimizerKey(string? userId) =>
        $"cortex-{(string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId)}";

    // Build a fixed-shape feature vector for routing decisions. Five
    // features chosen so the model has cheap signal without needing a
    // LearnedFeatureArchitect: prompt length, prompt token-ish count,
    // hour of day, day of week, installed-neuron count. The schema
    // is stable so trained models survive re-activations.
    static float[] BuildRoutingFeatures(string prompt, IReadOnlyList<INeuronDefinition> neurons)
    {
        var now = DateTimeOffset.UtcNow;
        var charCount = prompt.Length;
        var wordCount = 0;
        var inWord = false;
        for (var i = 0; i < prompt.Length; i++)
        {
            if (char.IsWhiteSpace(prompt[i])) inWord = false;
            else if (!inWord) { inWord = true; wordCount++; }
        }
        return
        [
            charCount,
            wordCount,
            now.Hour,
            (int)now.DayOfWeek,
            neurons.Count,
        ];
    }

    async Task<OptimizationResult?> PredictWillRouteAsync(
        string? userId,
        float[] features,
        CancellationToken ct)
    {
        try
        {
            var optimizer = grainFactory.GetGrain<INeuronOptimizer>(OptimizerKey(userId));
            return await optimizer.Predict(features);
        }
        catch (Exception ex)
        {
            // Optimizer is best-effort. Any failure (no grain registered in
            // the test silo, transport blip, training-time exception) keeps
            // routing on the LLM-classifier path.
            log.LogDebug(ex, "Cortex: optimizer Predict skipped for user {User}", userId);
            return null;
        }
    }

    async Task RecordRoutingDecisionAsync(
        string? userId,
        float[] features,
        bool routed,
        CancellationToken ct,
        string? prompt = null,
        RoutingSource source = RoutingSource.Unrouted,
        string? neuronId = null,
        double? mlPrediction = null,
        double? mlConfidence = null,
        bool llmCalled = false,
        string? correlationId = null,
        int durationMs = 0)
    {
        try
        {
            var optimizer = grainFactory.GetGrain<INeuronOptimizer>(OptimizerKey(userId));
            await optimizer.Record(new DecisionRecord(features, routed, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // Same posture as Predict — never break routing on a recording
            // failure. Drop the row, keep the user's response intact.
            log.LogDebug(ex,
                "Cortex: optimizer Record skipped for user {User} (routed={Routed})",
                userId, routed);
        }

        // Write to the CortexJournal for the inspector Routing tab.
        // Fire-and-forget — never slow the routing hot path on a journal write.
        if (prompt is not null && userId is not null)
        {
            try
            {
                var journal = grainFactory.GetGrain<ICortexJournal>("singleton");
                var decision = new RoutingDecision(
                    Prompt: prompt,
                    Source: source,
                    NeuronId: neuronId,
                    Confidence: null,
                    At: DateTimeOffset.UtcNow,
                    MlPrediction: mlPrediction,
                    MlConfidence: mlConfidence,
                    LlmCalled: llmCalled,
                    RoutingDurationMs: durationMs,
                    CorrelationId: correlationId ?? string.Empty);
                _ = journal.RecordAsync(userId, decision);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex,
                    "Cortex: CortexJournal write skipped for user {User}", userId);
            }
        }
    }

    // Walks the prompt corpus: for each installed neuron that has tagged
    // regex patterns, try them against the prompt. First hit wins. Returns
    // null if no installed-and-installed-handler neuron matches.
    async Task<FastPathHit?> TryFastPathAsync(
        string prompt,
        IReadOnlyList<INeuronDefinition> neurons,
        NeuronContext ctx,
        CancellationToken ct)
    {
        if (corpus.Count == 0) return null;

        foreach (var neuron in neurons)
        {
            if (!corpus.ByNeuron.TryGetValue(neuron.Id, out var patterns)) continue;
            foreach (var pattern in patterns)
            {
                if (!Regex.IsMatch(prompt, pattern.Pattern, RegexOptions.IgnoreCase)) continue;
                if (await TryRouteToAsync(neuron, prompt, "regex", pattern.ScenarioName, ctx, ct)
                    is { } result)
                    return new FastPathHit(result, pattern.ScenarioName);
                // Synapse type unknown shape or neuron uninstalled — keep
                // walking the corpus; another pattern/neuron may match.
            }
        }
        return null;
    }

    // JSON-mode classifier: the model picks one of the installed neuron
    // ids (or null) given the prompt + a one-line description per neuron.
    // Bdd-mock isn't a real classifier, so under INO_TEST_MODE this
    // path returns null — the corpus regex path handles routing in tests.
    // In production the user's xAI factory is plumbed in.
    async Task<NeuronResult?> TryClassifyWithLlmAsync(
        string prompt,
        IReadOnlyList<INeuronDefinition> neurons,
        NeuronContext ctx,
        CancellationToken ct)
    {
        var routableNeurons = neurons
            .Where(CanConstructSynapse)
            .ToArray();
        if (routableNeurons.Length == 0) return null;

        var systemMessage = BuildClassifierSystemMessage(routableNeurons);
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
        };

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, systemMessage),
                    new ChatMessage(ChatRole.User, prompt),
                },
                options,
                ct);
        }
        catch (Exception ex) when (ex is BddMockMissException or NotSupportedException)
        {
            // Mock provider can't classify; corpus regex was the only routing
            // signal under test mode. Real production providers either return
            // a JSON answer or throw a transport error — those propagate.
            log.LogDebug("Cortex LLM classifier skipped: {Reason}", ex.GetType().Name);
            return null;
        }

        if (TryParseClassifiedNeuronId(response, out var neuronId) &&
            routableNeurons.FirstOrDefault(e => e.Id == neuronId) is { } chosen)
        {
            return await TryRouteToAsync(chosen, prompt, "llm", "json-classifier", ctx, ct);
        }
        return null;
    }

    static string BuildClassifierSystemMessage(IReadOnlyList<INeuronDefinition> neurons)
    {
        var lines = neurons.Select(e => $"- {e.Id.Value}: {e.Description}");
        return
            "Classify the user's intent into one of the following neuron ids. " +
            "Respond ONLY with a JSON object of shape {\"neuronId\": \"<id>\"} " +
            "where <id> is one of the listed ids, or {\"neuronId\": null} when " +
            "none of the neurons fit.\n" +
            "Available neurons:\n" +
            string.Join("\n", lines);
    }

    static bool TryParseClassifiedNeuronId(ChatResponse response, out NeuronId neuronId)
    {
        neuronId = default;
        var text = response.Text;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("neuronId", out var idProp)) return false;
            if (idProp.ValueKind == JsonValueKind.Null) return false;
            var raw = idProp.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            neuronId = NeuronId.From(raw);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Routing tail. Hands control off to the neuron's plan grain
    // (resolved via IGrainFactory) which walks the neuron graph and returns
    // a final NeuronResult. Returns null when the canonical handler isn't
    // installed, the neuron declares no plan, or the plan grain can't be
    // resolved — the caller then keeps walking the corpus or falls through to UnroutedIntent.
    async Task<NeuronResult?> TryRouteToAsync(
        INeuronDefinition neuron,
        string prompt,
        string source,
        string scenarioName,
        NeuronContext ctx,
        CancellationToken ct)
    {
        var synapseType = neuron.CanonicalSynapseType;
        var canonical = await discovery.LookupCanonicalAsync(synapseType, ct);
        if (canonical is null)
        {
            log.LogDebug(
                "Cortex matched {NeuronDefinition} via {Source} but canonical handler not installed",
                neuron.Id, source);
            return null;
        }

        await AnnotateReasoningAsync(prompt, canonical.GrainType.FullName!, ct);

        if (neuron.PlanType is { } planType)
            return await TryExecutePlanAsync(neuron, planType, prompt, ctx, ct);

        log.LogDebug(
            "Cortex matched {NeuronDefinition} via {Source} but it declares no PlanType — skipping",
            neuron.Id, source);
        return null;
    }

    // Plan-dispatch tail. Resolves the plan grain via IGrainFactory using the
    // user id (or correlation id when no user is bound) as primary key, then
    // calls INeuronPlan.ExecuteAsync. The plan runs on its declared silo
    // (typically pinned to its owning domain) and drives the BFS from there.
    async Task<NeuronResult?> TryExecutePlanAsync(
        INeuronDefinition neuron,
        Type planType,
        string prompt,
        NeuronContext ctx,
        CancellationToken ct)
    {
        if (!typeof(INeuronPlan).IsAssignableFrom(planType))
        {
            log.LogError(
                "Cortex: neuron {NeuronDefinition} declared PlanType {PlanType} which does not extend INeuronPlan",
                neuron.Id, planType.FullName);
            return null;
        }

        var key = !string.IsNullOrWhiteSpace(ctx.UserId)
            ? ctx.UserId
            : ctx.CorrelationId.Value;
        var grainRef = grainFactory.GetGrain(planType, key);
        if (grainRef is not INeuronPlan plan)
        {
            log.LogError(
                "Cortex: GrainFactory could not resolve {PlanType} for neuron {NeuronDefinition}",
                planType.FullName, neuron.Id);
            return null;
        }

        var input = new NeuronPlanContext(prompt, ctx with { NeuronId = neuron.Id }, neuron.Id);
        var result = await plan.ExecuteAsync(input, ct);

        // Slice 4 — when the plan returns an RFW payload, register the
        // correlation_id → (plan interface AQN, grain key) so subsequent
        // RfwEvent RPCs can dispatch back to this activation via the typed
        // interface.
        if (result.Success && result.Rfw is not null)
        {
            try
            {
                var aqn = planType.AssemblyQualifiedName!;
                var registry = grainFactory.GetGrain<ICorrelationRegistry>("singleton");
                await registry.RegisterAsync(ctx.CorrelationId.Value, aqn, key);
                log.LogInformation(
                    "Cortex: registered RFW correlation {CorrelationId} -> {PlanType}({Key})",
                    ctx.CorrelationId.Value, planType.FullName, key);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex,
                    "Cortex: failed to register RFW correlation {CorrelationId} for plan {PlanType} — RfwEvent callbacks will fall back to friendly-text",
                    ctx.CorrelationId.Value, planType.FullName);
            }
        }

        return result;
    }

    static bool CanConstructSynapse(INeuronDefinition neuron) =>
        neuron.PlanType is not null;

    // Records which BDD scenario the prompt would match under the resolved
    // target neuron. Lights up the inspector Reasoning panel via IReasoningProbe
    // on the BddMockChatClient code path. Misses are advisory — never short-circuit routing.
    async Task AnnotateReasoningAsync(string prompt, string neuronId, CancellationToken ct)
    {
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [BddMockChatClient.NeuronIdKey] = neuronId,
            },
        };
        try
        {
            await chatClient.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, prompt) }, options, ct);
        }
        catch (BddMockMissException)
        {
            // Reasoning panel stays blank for this neuron — routing already won.
        }
        catch (NotSupportedException)
        {
            // Streaming-only provider; ignore.
        }
    }

    async Task<NeuronResult> EmitUnroutedAsync(string prompt, NeuronContext ctx, CancellationToken ct)
    {
        var unrouted = new UnroutedIntent(prompt, ctx.UserId ?? string.Empty);
        await firePort.FireBroadcast(unrouted, ctx, ct);
        log.LogInformation("Cortex unrouted {Text} for user {UserId}", prompt, ctx.UserId);
        return NeuronResult.Ok("No specialist is installed for that intent yet.").With(unrouted);
    }
}
