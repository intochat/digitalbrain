using DigitalBrain.Kernel.Voice;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class Voice2TextAttributeTests
{
    [Fact]
    public async Task MapperResolvesTheKeyedTranscriberForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expected = new FakeTranscriber();
        services.AddKeyedSingleton<IVoiceTranscriber>(TestVoiceModel.ServiceKey, expected);
        var provider = services.BuildServiceProvider();

        var mapper = new Voice2TextAttributeMapper<TestVoiceModel>();
        var parameter = typeof(FakeVoiceGrain).GetConstructors()[0].GetParameters()[0];
        var factory = mapper.GetFactory(parameter, new Voice2TextAttribute<TestVoiceModel>());

        var resolved = factory(new FakeGrainContext(provider));

        Assert.Same(expected, resolved);
        await Task.CompletedTask;
    }

    [Fact]
    public void ThrowsAClearErrorWhenAppliedToTheWrongParameterType()
    {
        var mapper = new Voice2TextAttributeMapper<TestVoiceModel>();
        var parameter = typeof(FakeVoiceGrainWithWrongParameterType).GetConstructors()[0].GetParameters()[0];

        var ex = Assert.Throws<ArgumentException>(() => mapper.GetFactory(parameter, new Voice2TextAttribute<TestVoiceModel>()));

        Assert.Contains("IVoiceTranscriber", ex.Message);
    }

    private sealed class TestVoiceModel
    {
        // LlmServiceKeys.For reflects for a public static member literally named "ServiceKey" — this is
        // the same reflection contract Task 5's TestModel.ServiceKey exercises, just for voice models.
        public const string ServiceKey = "openai-compatible-whisper-test";
    }

    private sealed class FakeTranscriber : IVoiceTranscriber
    {
        public Task<VoiceTranscriptionResult> TranscribeAsync(VoiceTranscriptionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeVoiceGrain(IVoiceTranscriber transcriber) { }
    private sealed class FakeVoiceGrainWithWrongParameterType(string notATranscriber) { }
}
