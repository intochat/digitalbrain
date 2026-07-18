using System.Text.RegularExpressions;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Recall.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Recall.Plans;

/// <summary>
/// Plan for the <c>recall.search</c> neuron. Strips the leading recall
/// verbs ("what did I tell you about", "do you remember") so the residual
/// is the actual question, then fires <see cref="RecallQuestion"/> for
/// <c>RecallNeuron</c> to handle.
///
/// Body extracted as <see langword="static"/> for unit-testability — same
/// pattern as Phase 3 Slice B's <c>OrderRideHomePlan</c>.
/// </summary>
public sealed class RecallPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<RecallPlan> log) : Grain, IRecallPlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var engine = new TraversalEngine(grainFactory, firePort, ctx, chatClient);
        return ExecuteAsync(input.Prompt, engine, ct);
    }

    public static Task<NeuronResult> ExecuteAsync(
        string prompt,
        ITraversalEngine engine,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(engine);

        var question = StripRecallPrefix(prompt);
        return engine.FireAsync(new RecallQuestion(question), ct);
    }

    static readonly Regex PrefixRegex = new(
        @"^(?:please\s+)?(?:do you (?:remember|recall)|what did i (?:say|tell you) about|what did i (?:say|tell you)|recall|remember)\s*[:,]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Trims the recall verb so the question routed downstream is just the
    /// substance. Leaves the original prompt unchanged when no prefix matches.
    /// </summary>
    public static string StripRecallPrefix(string prompt)
    {
        var trimmed = prompt.Trim();
        var stripped = PrefixRegex.Replace(trimmed, string.Empty).Trim();
        return string.IsNullOrEmpty(stripped) ? trimmed : stripped;
    }
}
