using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ino.Testing;

/// <summary>
/// Deterministic IChatClient that serves pre-recorded responses from a YAML file.
/// Regex-matches the last user message in the ChatRequest against recording patterns
/// and returns the first matching recording's text. Throws MockLlmMissException when
/// no recording matches — tests see a loud failure with the unmatched prompt fragment.
///
/// Phase 1 scope: text responses only. Phase 4 extends with tool-call and structured
/// JSON responses.
/// </summary>
public sealed class RecordedMockChatClient : IChatClient
{
    private readonly List<LlmRecording> _recordings = new();
    private readonly List<string> _unmatchedPrompts = new();

    public IReadOnlyList<string> UnmatchedPrompts => _unmatchedPrompts;

    /// <summary>Load recordings from a YAML file on disk.</summary>
    public void LoadRecordingsFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        LoadRecordingsFromYaml(yaml);
    }

    /// <summary>Load recordings from an inline YAML string.</summary>
    public void LoadRecordingsFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var loaded = deserializer.Deserialize<List<LlmRecording>>(yaml) ?? new();
        _recordings.AddRange(loaded);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages
            .LastOrDefault(m => m.Role == ChatRole.User)
            ?.Text ?? string.Empty;

        foreach (var recording in _recordings)
        {
            if (Regex.IsMatch(lastUserMessage, recording.Match, RegexOptions.IgnoreCase))
            {
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, recording.Text ?? string.Empty));
                return Task.FromResult(response);
            }
        }

        _unmatchedPrompts.Add(lastUserMessage);
        throw new MockLlmMissException(
            $"No recorded response matched prompt:\n---\n{lastUserMessage}\n---\n\n" +
            $"Add a recording to mocks/llm.recordings.yml with match pattern matching this prompt.",
            lastUserMessage);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "RecordedMockChatClient does not support streaming. Use GetResponseAsync instead.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
