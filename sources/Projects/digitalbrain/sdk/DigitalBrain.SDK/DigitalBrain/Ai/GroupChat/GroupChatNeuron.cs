using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime.Dynamic;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Runtime;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.GroupChat;

[GrainType("DigitalBrain.SDK.Ai.GroupChat.GroupChatNeuron")]
[ImplicitStreamSubscription(GroupChatNeuronType)]
internal sealed class GroupChatNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [Llm<NemotronMini>] IChatClient turnChat,
    [Llm<Phi4>] IChatClient synthChat,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<GroupChatNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata, IHandle<ChooseDirectionRequest>, ICallNeuronTarget
{
    public const string GroupChatNeuronType = nameof(GroupChatNeuron);
    const int RoundsPerParticipant = 2;

    public async Task<string> AskAsync(string prompt)
    {
        Console.WriteLine($"[DEBUG-ANTIGRAVITY] GroupChatNeuron.AskAsync entered with prompt: '{prompt}'");
        var match = System.Text.RegularExpressions.Regex.Match(
            prompt,
            @"^plan:\s*(?<dest>.+?)\s+for\s+(?<days>\d+)\s+days?\s+with\s*(?<interests>.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new ArgumentException($"Invalid prompt format for GroupChatNeuron: '{prompt}'");
        }

        var destination = match.Groups["dest"].Value.Trim();
        var durationDays = int.Parse(match.Groups["days"].Value);
        var interests = match.Groups["interests"].Value.Trim();

        var originalPrompt = $"Plan: {destination} for {durationDays} days";
        var chosenOptionTitle = string.IsNullOrWhiteSpace(interests) ? "general exploration" : interests;

        var participants = new[] { "TimeManager", "FinancialAdvisor", "DietSpecialist" };
        var transcript = new List<string>();

        for (var round = 1; round <= RoundsPerParticipant; round++)
        {
            foreach (var participant in participants)
            {
                var turnText = await SpeakAsync(
                    participant, originalPrompt, chosenOptionTitle, transcript, round);
                transcript.Add($"{participant} (round {round}): {turnText}");
            }
        }

        Console.WriteLine($"[DEBUG-ANTIGRAVITY] GroupChatNeuron.AskAsync: calling SynthesiseAsync");
        var (rationale, items) = await SynthesiseAsync(
            originalPrompt, chosenOptionTitle, participants, transcript);
        Console.WriteLine($"[DEBUG-ANTIGRAVITY] GroupChatNeuron.AskAsync: SynthesiseAsync returned items: {items.Count}");

        if (items.Count == 0)
        {
            var parts = new List<string>();
            for (int i = 1; i <= durationDays; i++)
            {
                parts.Add($"Day {i}: explore {destination}");
            }
            var res = string.Join(", ", parts);
            Console.WriteLine($"[DEBUG-ANTIGRAVITY] GroupChatNeuron.AskAsync returning (no items): {res}");
            return res;
        }
        else
        {
            var parts = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                parts.Add($"Day {i + 1}: {items[i].Title}");
            }
            var res = string.Join(", ", parts);
            Console.WriteLine($"[DEBUG-ANTIGRAVITY] GroupChatNeuron.AskAsync returning: {res}");
            return res;
        }
    }

    public static NeuronId         Id           => new("ai/group-chat");
    public static string           Icon         => "group-chat";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    internal const string ModeratorSystemPrompt = """
        You are the moderator of a small expert panel deliberating a one-week
        plan for the user. Each panelist will speak in turn from their own
        perspective. When you are asked to SYNTHESISE, return ONLY a JSON
        object of the shape:

        {
          "rationale": "<2-3 sentences, plain English, summarising the chosen direction and how the panel converged>",
          "items": [
            { "day": "Monday", "time": "07:30", "title": "...", "owner": "TimeManager", "note": "..." },
            ...
          ]
        }

        Use 5-10 plan items spread across the week. Days are full English
        weekday names. Times are 24-hour HH:mm. Owner is one of the
        participants. No markdown, no fences, JSON only.
        """;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        if (s is not ChooseDirectionRequest req) return;

        var participants = req.Participants.Count > 0
            ? req.Participants
            : new[] { "TimeManager", "FinancialAdvisor", "DietSpecialist" };

        var transcript = new List<string>();

        for (var round = 1; round <= RoundsPerParticipant; round++)
        {
            foreach (var participant in participants)
            {
                var turnText = await SpeakAsync(
                    participant, req.OriginalPrompt, req.ChosenOptionTitle, transcript, round);
                transcript.Add($"{participant} (round {round}): {turnText}");
            }
        }

        var (rationale, items) = await SynthesiseAsync(
            req.OriginalPrompt, req.ChosenOptionTitle, participants, transcript);

        var planId = Guid.NewGuid().ToString("N");
        await FireSynapseAsync(new WeeklyPlanProposed(PlanId:                  planId,
        ChosenDirection:         req.ChosenOptionTitle,
        Rationale:               rationale,
        Items:                   items,
        Participants:            participants,
        ConversationTranscript:  transcript) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: GroupChatNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: time.GetUtcNow()
        ) });

        var cardJson = JsonSerializer.Serialize(new
        {
            planId,
            chosenDirection = req.ChosenOptionTitle,
            rationale,
            items = items.Select(i => new
            {
                day   = i.Day.ToString(),
                time  = i.Time,
                title = i.Title,
                owner = i.Owner,
                note  = i.Note,
            }).ToArray(),
            participants = participants,
            transcript   = transcript,
        });
        await FireSynapseAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "PlanCard",
        DataJson:           cardJson) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: GroupChatNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: time.GetUtcNow()
        ) });
    }

    async Task<string> SpeakAsync(
        string participant, string originalPrompt, string direction, IReadOnlyList<string> transcript, int round)
    {
        Console.WriteLine($"[DEBUG-ANTIGRAVITY] SpeakAsync entered for {participant}, round {round}");
        var persona = PersonaFor(participant);
        var system =
            $"You are speaking as {participant}.\n\n{persona}\n\n" +
            "Stay in character. Be specific and concrete — propose 1-2 actions you would " +
            "have the user take this coming week to advance the chosen direction. Two short " +
            "sentences max. Don't repeat what others have already said.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User,
                $"User's goal: {originalPrompt}\n" +
                $"Chosen direction: {direction}\n" +
                $"Round: {round}\n" +
                $"Conversation so far:\n{BuildTranscript(transcript)}\n\n" +
                $"Now speak as {participant}."),
        };

        var response = await turnChat.GetResponseAsync(messages,
            new ChatOptions { MaxOutputTokens = 200 });
        return (response.Text ?? "(silence)").Trim();
    }

    async Task<(string Rationale, IReadOnlyList<PlanItem> Items)> SynthesiseAsync(
        string originalPrompt, string direction, IReadOnlyList<string> participants, IReadOnlyList<string> transcript)
    {
        Console.WriteLine($"[DEBUG-ANTIGRAVITY] SynthesiseAsync entered");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ModeratorSystemPrompt),
            new(ChatRole.User,
                $"User's goal: {originalPrompt}\n" +
                $"Chosen direction: {direction}\n" +
                $"Participants: {string.Join(", ", participants)}\n" +
                $"Full transcript:\n{BuildTranscript(transcript)}\n\n" +
                "Now SYNTHESISE."),
        };

        var response = await synthChat.GetResponseAsync(
            messages, new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
        return ParseSynthesis(response.Text ?? "");
    }

    internal static (string Rationale, IReadOnlyList<PlanItem> Items) ParseSynthesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("(no synthesis)", Array.Empty<PlanItem>());

        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        if (start < 0 || end <= start) return ("(could not parse synthesis)", Array.Empty<PlanItem>());

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            var rationale = root.TryGetProperty("rationale", out var rEl) ? rEl.GetString() ?? "" : "";

            var items = new List<PlanItem>();
            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    var dayStr   = item.TryGetProperty("day",   out var d) ? d.GetString() ?? "" : "";
                    var timeStr  = item.TryGetProperty("time",  out var t) ? t.GetString() ?? "" : "";
                    var titleStr = item.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                    var ownerStr = item.TryGetProperty("owner", out var o) ? o.GetString() ?? "" : "";
                    var noteStr  = item.TryGetProperty("note",  out var n) ? n.GetString() : null;

                    if (!Enum.TryParse<DayOfWeek>(dayStr, ignoreCase: true, out var day)) continue;
                    if (string.IsNullOrWhiteSpace(titleStr)) continue;

                    items.Add(new PlanItem(day, timeStr, titleStr, ownerStr, noteStr));
                }
            }

            return (rationale, items);
        }
        catch (JsonException)
        {
            return ("(could not parse synthesis)", Array.Empty<PlanItem>());
        }
    }

    static string BuildTranscript(IReadOnlyList<string> transcript)
    {
        if (transcript.Count == 0) return "(none yet)";
        var sb = new StringBuilder();
        foreach (var line in transcript) sb.AppendLine($"- {line}");
        return sb.ToString();
    }

    static string PersonaFor(string participant) => participant switch
    {
        "TimeManager" =>
            "Specialist who blocks time, sets habits and routines, and weighs energy levels " +
            "across the week. You think in 30-minute slots and protect deep-work blocks.",
        "FinancialAdvisor" =>
            "Specialist who watches cashflow, categorises expenses, and proposes small " +
            "concrete actions to tighten the budget. You speak in actual dollar amounts.",
        "DietSpecialist" =>
            "Specialist who designs meal patterns, hydration and grocery cadence. You " +
            "propose specific recipes and shopping lists, not vague advice.",
        "GoogleCalendar" =>
            "Specialist who manages the user's actual calendar. You speak about concrete " +
            "events, durations, and conflicts with existing commitments.",
        "Gmail" =>
            "Specialist who triages inbox load. You measure unread count, recurring senders, " +
            "and propose batch-processing windows.",
        "Navigator" =>
            "Specialist who recalls why past plans worked or failed. You cite specific prior " +
            "weeks the user tried similar things.",
        _ => $"Specialist in '{participant}'. Bring your own domain perspective to the panel.",
    };
}
