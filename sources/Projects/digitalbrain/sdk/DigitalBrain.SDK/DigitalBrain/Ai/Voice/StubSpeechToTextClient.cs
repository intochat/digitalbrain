using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Voice;

// Reads the input audio stream as UTF-8 text and returns it as the transcript.
// Tests put the expected transcript into the request's Audio bytes so the round-trip
// is observable without depending on a real speech model.
internal sealed class StubSpeechToTextClient : ISpeechToTextClient
{
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(audioSpeechStream, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new SpeechToTextResponse(text);
    }

    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(audioSpeechStream, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        yield return new SpeechToTextResponseUpdate(text)
        {
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(1),
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
