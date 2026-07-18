using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Reminders.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Reminders.Plans;

/// <summary>
/// Plan for the <c>reminders.cancel</c> neuron. Walks the user's
/// reminder journal for the most recent <see cref="ReminderSet"/> whose
/// description fuzzy-matches the prompt, then calls
/// <see cref="IRemindersNeuron.CancelAsync"/>. Match strategy is keyword-
/// overlap (case-insensitive) — good enough for v0.1; LLM disambiguation
/// can be added when "the trash one" / "the second one" prompts surface.
/// </summary>
public sealed class CancelReminderPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<CancelReminderPlan> log) : Grain, ICancelReminderPlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var userKey = !string.IsNullOrWhiteSpace(ctx.UserId) ? ctx.UserId : ctx.CorrelationId.Value;

        var engine = new TraversalEngine(grainFactory, firePort, ctx, chatClient);
        var neuron = grainFactory.GetGrain<IRemindersNeuron>(userKey);
        return ExecuteAsync(input.Prompt, ctx.CorrelationId.Value, userKey, engine, neuron, log, ct);
    }

    public static async Task<NeuronResult> ExecuteAsync(
        string prompt,
        string correlationId,
        string userKey,
        ITraversalEngine engine,
        IRemindersNeuron neuron,
        ILogger log,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(log);

        // Walk the reminder journal for ReminderSet entries that haven't been
        // cancelled. Most recent matching set wins.
        var history = await engine.VisitAsync<ReminderEvent>(
            userKey, RecallQuery<ReminderEvent>.All, ct);

        var liveSets = ResolveLiveSets(history);
        if (liveSets.Count == 0)
            return NeuronResult.Ok("You don't have any active reminders.");

        var promptTokens = Tokenize(prompt);
        var match = liveSets
            .Select(s => (Set: s, Score: ScoreMatch(promptTokens, Tokenize(s.Description))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Set.DueAt)
            .FirstOrDefault();

        if (match.Set is null)
        {
            log.LogInformation(
                "CancelReminderPlan: no live reminder matched prompt {Prompt} for user {User}",
                prompt, userKey);
            return NeuronResult.Ok(
                "I couldn't find a matching reminder to cancel. Try mentioning what it was about.");
        }

        var ok = await neuron.CancelAsync(match.Set.Name, correlationId);
        if (!ok)
            return NeuronResult.Ok("That reminder was already gone — nothing to cancel.");

        log.LogInformation(
            "CancelReminderPlan: cancelled reminder {Name} ({Description}) for user {User}",
            match.Set.Name, match.Set.Description, userKey);
        return NeuronResult.Ok($"Cancelled — I won't remind you to {match.Set.Description}.");
    }

    static IReadOnlyList<ReminderSet> ResolveLiveSets(IReadOnlyList<EventEnvelope<ReminderEvent>> history)
    {
        var live = new Dictionary<string, ReminderSet>(StringComparer.Ordinal);
        foreach (var env in history)
        {
            switch (env.Payload)
            {
                case ReminderSet set:
                    live[set.Name] = set;
                    break;
                case ReminderCancelled cancelled:
                    live.Remove(cancelled.Name);
                    break;
                case ReminderDue due:
                    live.Remove(due.Name);
                    break;
            }
        }
        return live.Values.ToList();
    }

    static IReadOnlySet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '.', ',', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToHashSet();

    static int ScoreMatch(IReadOnlySet<string> a, IReadOnlySet<string> b) =>
        a.Intersect(b).Count();

    static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "to", "from", "with", "about", "cancel", "never",
        "mind", "forget", "remind", "reminder", "please",
    };
}
