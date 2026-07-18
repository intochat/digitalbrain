using System.Text.Json;
using System.Text.RegularExpressions;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Reminders.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Reminders.Plans;

/// <summary>
/// Plan for the <c>reminders.set</c> neuron. Extracts <c>(description, delay)</c>
/// from the user prompt then calls <see cref="IRemindersNeuron.SetAsync"/>.
/// Slot extraction is regex-first (catches "in 30 minutes" / "in 2 hours" /
/// "in 1 hour 15 minutes") with an LLM fallback for fuzzier shapes.
///
/// Body extracted as <see langword="static"/> for unit-testability — same
/// pattern as <c>OrderRideHomePlan</c>: a real <see cref="IRemindersNeuron"/>
/// substitute drives the test, no grain activation needed.
/// </summary>
public sealed class SetReminderPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<SetReminderPlan> log) : Grain, ISetReminderPlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var userKey = !string.IsNullOrWhiteSpace(ctx.UserId) ? ctx.UserId : ctx.CorrelationId.Value;
        var neuron = grainFactory.GetGrain<IRemindersNeuron>(userKey);
        return ExecuteAsync(input.Prompt, ctx.CorrelationId.Value, neuron, chatClient, log, ct);
    }

    /// <summary>
    /// Pure plan body. Tests drive it directly with a substituted neuron +
    /// chat client.
    /// </summary>
    public static async Task<NeuronResult> ExecuteAsync(
        string prompt,
        string correlationId,
        IRemindersNeuron neuron,
        IChatClient chatClient,
        ILogger log,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(log);

        var slots = TryExtractSlotsByRegex(prompt) ?? await ExtractSlotsByLlmAsync(prompt, chatClient, ct);
        if (slots is null)
        {
            log.LogInformation("SetReminderPlan: could not extract reminder slots from prompt {Prompt}", prompt);
            return NeuronResult.Ok(
                "I couldn't tell what to remind you about or when. Try 'remind me to call mom in 30 minutes'.");
        }

        var (description, delay) = slots.Value;
        if (delay <= TimeSpan.Zero)
            return NeuronResult.Ok("Reminders need a positive delay — try 'in 5 minutes' or 'in 1 hour'.");

        var name = await neuron.SetAsync(description, delay, correlationId);
        log.LogInformation(
            "SetReminderPlan: scheduled reminder {Name} ({Description}) in {Delay}",
            name, description, delay);

        return NeuronResult.Ok(
            $"OK — I'll remind you to {description} in {FormatDelay(delay)}.");
    }

    static readonly Regex InRegex = new(
        @"^(?<desc>.+?)\s+in\s+(?<n>\d+)\s*(?<unit>m|min|mins|minute|minutes|h|hr|hrs|hour|hours|s|sec|secs|second|seconds)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex LeadingPrefixRegex = new(
        @"^(?:please\s+)?(?:remind me to|set a reminder to|set reminder to|remind me|set a reminder|set reminder)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static (string Description, TimeSpan Delay)? TryExtractSlotsByRegex(string prompt)
    {
        var stripped = LeadingPrefixRegex.Replace(prompt.Trim(), string.Empty).Trim();
        var match = InRegex.Match(stripped);
        if (!match.Success) return null;

        var desc = match.Groups["desc"].Value.Trim().TrimEnd('.', '!', '?', ',');
        if (!int.TryParse(match.Groups["n"].Value, out var n)) return null;
        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var delay = unit switch
        {
            "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(n),
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(n),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(n),
            _ => TimeSpan.Zero,
        };
        return delay > TimeSpan.Zero ? (desc, delay) : null;
    }

    static async Task<(string Description, TimeSpan Delay)?> ExtractSlotsByLlmAsync(
        string prompt, IChatClient chatClient, CancellationToken ct)
    {
        var system =
            "Extract a reminder description and delay from the user's prompt. " +
            "Reply ONLY with JSON of shape {\"description\": \"<text>\", \"delaySeconds\": <int>} " +
            "where delaySeconds is total seconds (>0). If you can't extract both, reply " +
            "{\"description\": null, \"delaySeconds\": 0}.";
        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, system),
                    new ChatMessage(ChatRole.User, prompt),
                },
                new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                ct);
        }
        catch (Exception ex) when (ex is BddMockMissException or NotSupportedException)
        {
            return null;
        }

        var text = response.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("description", out var d) ||
                d.ValueKind == JsonValueKind.Null) return null;
            if (!doc.RootElement.TryGetProperty("delaySeconds", out var s)) return null;
            var description = d.GetString();
            if (string.IsNullOrWhiteSpace(description)) return null;
            var seconds = s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            if (seconds <= 0) return null;
            return (description.Trim(), TimeSpan.FromSeconds(seconds));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string FormatDelay(TimeSpan delay)
    {
        if (delay.TotalHours >= 1)
        {
            var hours = (int)delay.TotalHours;
            var minutes = delay.Minutes;
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }
        if (delay.TotalMinutes >= 1) return $"{(int)delay.TotalMinutes} minute{(delay.TotalMinutes >= 2 ? "s" : "")}";
        return $"{(int)delay.TotalSeconds} second{(delay.TotalSeconds >= 2 ? "s" : "")}";
    }
}
