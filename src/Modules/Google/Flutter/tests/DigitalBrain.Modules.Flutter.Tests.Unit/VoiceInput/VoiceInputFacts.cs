using DigitalBrain.Flutter;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.VoiceInput.Signals;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.VoiceInput;

public sealed class VoiceInputFacts
{
    [Fact]
    public async Task CapturedAudioIsAnnouncedWithoutTranscribing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var voice = brain.Get<IVoiceInput>("mic");
        await voice.Configure("Hold to talk");
        await using var captured = await brain.Observe<VoiceCaptured>(voice, ct);

        await voice.Capture([1, 2, 3], "audio/webm");

        var capture = await captured.NextAsync(ct: ct);
        Assert.Equal([1, 2, 3], capture.Audio);
        Assert.Equal("audio/webm", capture.MimeType);
        Assert.Equal(1, (await voice.Read()).Captures);
        await Assert.ThrowsAsync<ArgumentException>(() => voice.Capture([], "audio/webm"));
        await Assert.ThrowsAsync<ArgumentException>(() => voice.Capture([1], "text/plain"));
    }
}
