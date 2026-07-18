using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.GroupChat;
using DigitalBrain.SDK.DigitalBrain.Mcp;
using Google.Protobuf;
using Grpc.Core;
using ModelContextProtocol.Server;

namespace DigitalBrain.SDK.DigitalBrain.Mcp.Tools;

[McpServerToolType]
internal sealed class BrainTools(
    DigitalBrainGateway.DigitalBrainGatewayClient gateway,
    BrainWatch.BrainWatchClient brainWatch)
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "brain")]
    [Description("Send a plain-English prompt to DigitalBrain (the same entrypoint a human " +
        "uses in the Flutter dock). DigitalBrain engineers a tested neuron for it. Returns a " +
        "JSON bundle: outcome, neuronId, the generated .feature, code, test result, and " +
        "the RFW cards produced. Note: on a failed build, neuronId is the sentinel " +
        "\"dynamic/(failed)\" rather than a real id.")]
    public async Task<string> Brain(
        [Description("The plain-English software request.")] string prompt,
        [Description("Max seconds to wait for the create loop (default 180).")]
        int timeoutSeconds = 180,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        var collected = new List<CardView>();
        var timedOut = false;
        var brokeOut = false;

        try
        {
            var headers = new Metadata
            {
                { "x-brain-id", "primary" },
                { "x-active-scope", "primary" }
            };

            using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var feed = gateway.WatchHomeFeed(
                new WatchHomeFeedRequest(), headers: headers, cancellationToken: feedCts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var reader = feed.ResponseStream;

            var submit = await gateway.SubmitPromptAsync(new SubmitPromptRequest
            {
                Text = prompt,
                UserId = "claude-code",
                CorrelationId = correlationId.ToString(),
            }, headers: headers, cancellationToken: ct);
            var cid = submit.CorrelationId;

            while (DateTime.UtcNow < deadline)
            {
                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(feedCts.Token);
                stepCts.CancelAfter(TimeSpan.FromSeconds(
                    Math.Max(1, (deadline - DateTime.UtcNow).TotalSeconds)));

                bool moved;
                try { moved = await reader.MoveNext(stepCts.Token); }
                catch (OperationCanceledException) { timedOut = true; break; }
                if (!moved) { brokeOut = true; break; }

                var env = reader.Current;
                if (env.CorrelationId != cid) continue;

                var card = new CardView(env.RootWidget, env.DataJson);
                collected.Add(card);
                if (CardFold.IsTerminal(card)) { brokeOut = true; break; }
            }

            if (!brokeOut && !timedOut) timedOut = true;

            feedCts.Cancel();
            var result = CardFold.Reduce(collected, timedOut);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (RpcException ex) when (
            ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            Console.WriteLine($"[MCP_ERROR] gRPC exception in brain: {ex.Status.Detail}, StatusCode={ex.StatusCode}, Message={ex.Message}");
            var unavailable = new BrainResult(
                "unavailable", null, null, null,
                $"DigitalBrain gateway not reachable — run `aspire start` first. Details: {ex.StatusCode}: {ex.Message}",
                Array.Empty<CardView>());
            return JsonSerializer.Serialize(unavailable, JsonOptions);
        }
    }

    [McpServerTool(Name = "convene")]
    [Description("Convene the multi-agent expert panel (GroupChatNeuron) directly. Routes a " +
        "ChooseDirectionRequest across the cortex; the panel deliberates — each specialist speaks " +
        "in turn on the local turn-model, the moderator synthesises on the synthesis-model — and a " +
        "PlanCard lands on the home feed. Unlike `brain` (which engineers a new neuron), this " +
        "exercises an existing multi-agent neuron. Returns JSON: outcome, correlationId, the " +
        "deliberated plan (rationale + items + transcript), and every card seen.")]
    public async Task<string> Convene(
        [Description("The user's original goal/prompt, e.g. 'Plan a budget surf week in Bali'.")]
        string prompt,
        [Description("Short label for the chosen direction. Defaults to the prompt.")]
        string direction = "",
        [Description("Comma-separated specialist participants joining the panel.")]
        string participants = "TimeManager,FinancialAdvisor,DietSpecialist",
        [Description("Max seconds to wait for the panel to deliberate (default 180).")]
        int timeoutSeconds = 180,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        var participantList = participants
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosenTitle = string.IsNullOrWhiteSpace(direction) ? prompt : direction;
        var cid = correlationId.ToString();

        try
        {
            var headers = new Metadata
            {
                { "x-brain-id", "primary" },
                { "x-active-scope", "primary" }
            };

            using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var feed = gateway.WatchHomeFeed(
                new WatchHomeFeedRequest(), headers: headers, cancellationToken: feedCts.Token);
            var reader = feed.ResponseStream;
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                CallerNeuronType   = "McpConvene",
                ReceiverNeuronType = GroupChatNeuron.GroupChatNeuronType,
                ChosenOptionId     = "mcp-convene",
                ChosenOptionTitle  = chosenTitle,
                OriginalPrompt     = prompt,
                Participants       = participantList,
            });

            var envelope = new SynapseEnvelope
            {
                CorrelationId = cid,
                TypeName      = typeof(ChooseDirectionRequest).FullName!,
                Payload       = ByteString.CopyFrom(payload),
            };

            // Primary path: the panel addresses WeeklyPlanProposed back to the gateway,
            // so Send returns it directly once the deliberation finishes within the
            // gateway's reply window (fast / warm runs). The home-feed subscription
            // opened above stays connected and buffers the PlanCard as a fallback for
            // slow runs where Send deadlines first.
            try
            {
                var reply = await gateway.SendAsync(envelope, headers: headers, cancellationToken: ct);
                if (reply.TypeName.EndsWith("WeeklyPlanProposed", StringComparison.Ordinal))
                {
                    feedCts.Cancel();
                    return JsonSerializer.Serialize(new
                    {
                        outcome       = "planned",
                        via           = "send-reply",
                        correlationId = cid,
                        plan          = ParseNode(reply.Payload.ToStringUtf8()),
                    }, JsonOptions);
                }
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded)
            {
                // Panel still fanning out (e.g. cold/slow local models). Fall through to
                // the buffered home-feed stream below.
            }

            var collected = new List<CardView>();
            while (DateTime.UtcNow < deadline)
            {
                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(feedCts.Token);
                stepCts.CancelAfter(TimeSpan.FromSeconds(
                    Math.Max(1, (deadline - DateTime.UtcNow).TotalSeconds)));

                bool moved;
                try { moved = await reader.MoveNext(stepCts.Token); }
                catch (OperationCanceledException) { break; }
                if (!moved) break;

                var env = reader.Current;
                if (env.CorrelationId != cid) continue;

                collected.Add(new CardView(env.RootWidget, env.DataJson));
                if (env.RootWidget == "PlanCard")
                {
                    feedCts.Cancel();
                    return JsonSerializer.Serialize(new
                    {
                        outcome       = "planned",
                        via           = "home-feed",
                        correlationId = cid,
                        plan          = ParseNode(env.DataJson),
                        cards         = collected,
                    }, JsonOptions);
                }
            }

            feedCts.Cancel();
            return JsonSerializer.Serialize(new
            {
                outcome       = "timeout",
                correlationId = cid,
                plan          = (JsonNode?)null,
                cards         = collected,
            }, JsonOptions);
        }
        catch (RpcException ex) when (
            ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            Console.WriteLine($"[MCP_ERROR] gRPC exception in convene: {ex.Status.Detail}, StatusCode={ex.StatusCode}, Message={ex.Message}");
            return JsonSerializer.Serialize(new
            {
                outcome = "unavailable",
                error   = $"DigitalBrain gateway not reachable — run `aspire start` first. Details: {ex.StatusCode}: {ex.Message}",
            }, JsonOptions);
        }
    }

    static JsonNode? ParseNode(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    [McpServerTool(Name = "design_ui")]
    [Description("Send a design prompt directly to the Grok UI Designer Neuron. " +
        "Returns the designed UI JSON, explanation, and InoCode.")]
    public async Task<string> DesignUi(
        [Description("The UI layout design prompt, e.g. 'Design a task dashboard'.")] string prompt,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        try
        {
            var headers = new Metadata
            {
                { "x-brain-id", "primary" },
                { "x-active-scope", "primary" }
            };

            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Prompt = prompt
            });

            var envelope = new SynapseEnvelope
            {
                CorrelationId = correlationId.ToString(),
                TypeName = "DigitalBrain.SDK.Ai.GrokUiDesignRequest",
                Payload = ByteString.CopyFrom(payloadBytes),
            };

            var reply = await gateway.SendAsync(envelope, headers: headers, cancellationToken: ct);

            if (reply.TypeName == "DigitalBrain.SDK.Ai.GrokUiDesignResponse")
            {
                var responseNode = ParseNode(reply.Payload.ToStringUtf8());
                return JsonSerializer.Serialize(new
                {
                    outcome = "designed",
                    correlationId = correlationId.ToString(),
                    uiJson = responseNode?["UiJson"]?.ToString(),
                    explanation = responseNode?["Explanation"]?.ToString(),
                    inoCode = responseNode?["InoCode"]?.ToString(),
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                outcome = "unexpected_reply",
                correlationId = correlationId.ToString(),
                replyType = reply.TypeName,
                rawPayload = reply.Payload.ToStringUtf8()
            }, JsonOptions);
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"[MCP_ERROR] gRPC exception in design_ui: {ex.Status.Detail}, StatusCode={ex.StatusCode}, Message={ex.Message}");
            return JsonSerializer.Serialize(new
            {
                outcome = "unavailable",
                error = $"DigitalBrain gateway not reachable — run `aspire start` first. Details: {ex.StatusCode}: {ex.Message}",
            }, JsonOptions);
        }
    }

    [McpServerTool(Name = "list_neurons")]
    [Description("Snapshot of neurons DigitalBrain currently knows (read-only). Use before " +
        "and after `brain` to see what changed.")]
    public async Task<string> ListNeurons(CancellationToken ct = default)
    {
        try
        {
            var headers = new Metadata
            {
                { "x-brain-id", "primary" },
                { "x-active-scope", "primary" }
            };
            var response = await brainWatch.SnapshotAsync(new SnapshotRequest(), headers: headers, cancellationToken: ct);
            var nodes = response.Nodes.Select(n => new
            {
                id = n.Id,
                domain = n.Domain,
                firstSeenAt = n.FirstSeenAt?.ToDateTime().ToString("O"),
                lastSeenAt = n.LastSeenAt?.ToDateTime().ToString("O"),
            });
            return JsonSerializer.Serialize(nodes, JsonOptions);
        }
        catch (RpcException ex) when (
            ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            Console.WriteLine($"[MCP_ERROR] gRPC exception: {ex.Status.Detail}, StatusCode={ex.StatusCode}, Message={ex.Message}");
            return JsonSerializer.Serialize(new
            {
                error = $"DigitalBrain gateway not reachable — run `aspire start` first. Details: {ex.StatusCode}: {ex.Message}",
            }, JsonOptions);
        }
    }
}
