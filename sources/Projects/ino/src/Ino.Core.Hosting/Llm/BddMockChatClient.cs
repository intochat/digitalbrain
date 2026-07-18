using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Deterministic <see cref="IChatClient"/> driven by Gherkin <c>.feature</c>
/// files embedded in installed neuron bundles. On construction, every
/// <c>*.feature</c> file found in the configured feature paths is parsed
/// (via <see cref="BddScenarioLoader"/>) into a regex-indexed scenario list.
/// <see cref="GetResponseAsync"/> regex-matches the last user message against
/// each scenario's <see cref="BddScenario.PromptPattern"/> and returns the
/// first match's <see cref="BddScenario.ReplyText"/>.
///
/// Each match is recorded to <see cref="IReasoningProbe"/> under the neuron
/// id the caller supplies via
/// <see cref="ChatOptions.AdditionalProperties"/>[<see cref="NeuronIdKey"/>]
/// — the Flutter inspector's Reasoning panel reads from the probe to surface
/// <c>mocked via BDD · {scenario}</c>. Misses throw
/// <see cref="BddMockMissException"/> so tests see a loud failure with the
/// unmatched prompt fragment.
///
/// Streaming is not supported (BDD scenarios are point-in-time responses).
/// </summary>
public sealed class BddMockChatClient : IChatClient
{
    public const string NeuronIdKey = "ino.neuron.id";

    readonly IReadOnlyList<BddScenario> _scenarios;
    readonly IReasoningProbe _probe;
    readonly ILogger<BddMockChatClient>? _log;
    readonly TimeProvider _time;

    public BddMockChatClient(
        IEnumerable<BddScenario> scenarios,
        IReasoningProbe probe,
        ILogger<BddMockChatClient>? log = null,
        TimeProvider? time = null)
    {
        _scenarios = scenarios.ToArray();
        _probe = probe;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<BddScenario> Scenarios => _scenarios;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var neuronId = TryGetNeuronId(options) ?? "<unattributed>";

        foreach (var scenario in _scenarios)
        {
            if (!Regex.IsMatch(lastUser, scenario.PromptPattern, RegexOptions.IgnoreCase))
                continue;

            var record = new ReasoningRecord(
                Source: "bdd-mock",
                ScenarioName: scenario.ScenarioName,
                FeatureTitle: scenario.FeatureTitle,
                Prompt: lastUser,
                Reply: scenario.ReplyText,
                Timestamp: _time.GetUtcNow());

            _probe.Record(neuronId, record);
            _log?.LogDebug(
                "bdd-mock matched neuron={NeuronId} feature={Feature} scenario={Scenario}",
                neuronId, scenario.FeatureTitle, scenario.ScenarioName);

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, scenario.ReplyText)));
        }

        _log?.LogInformation(
            "bdd-mock miss neuron={NeuronId} scenarios={ScenarioCount} prompt={Prompt}",
            neuronId, _scenarios.Count, lastUser);
        throw new BddMockMissException(lastUser, _scenarios.Count);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "BddMockChatClient does not support streaming. Use GetResponseAsync.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(BddMockChatClient) ? this : null;

    public void Dispose() { }

    static string? TryGetNeuronId(ChatOptions? options)
    {
        if (options?.AdditionalProperties is null) return null;
        return options.AdditionalProperties.TryGetValue(NeuronIdKey, out var v) && v is string s ? s : null;
    }
}

/// <summary>
/// Thrown when no <see cref="BddScenario"/>'s prompt pattern matches the
/// inbound chat prompt. The message embeds the unmatched prompt so a
/// scenario author can copy the phrase into a new feature file.
/// </summary>
public sealed class BddMockMissException : Exception
{
    public BddMockMissException(string unmatchedPrompt, int loadedScenarios)
        : base($"BddMockChatClient has {loadedScenarios} loaded scenario(s), none matched prompt:\n---\n{unmatchedPrompt}\n---\nAdd a Scenario to a Features/*.feature with a Given step quoting a regex that matches this prompt.")
    {
        UnmatchedPrompt = unmatchedPrompt;
        LoadedScenarios = loadedScenarios;
    }

    public string UnmatchedPrompt { get; }
    public int LoadedScenarios { get; }
}
