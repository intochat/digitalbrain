using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Voice;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class Voice2TextAttributeTests
{
    // Provider "test-provider" + Id "test-voice-model" contain no ':' or '.', so
    // DigitalBrainModelDescriptor.Normalize leaves them untouched beyond lowercasing (already lowercase).
    private const string TestVoiceModelServiceKey = "test-provider-test-voice-model";

    [Fact]
    public async Task MapperResolvesTheKeyedTranscriberForTheAttributesModelType()
    {
        var services = new ServiceCollection();
        var expected = new FakeTranscriber();
        services.AddKeyedSingleton<IVoiceTranscriber>(TestVoiceModelServiceKey, expected);
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

    private sealed class TestVoiceModel : VoiceToTextModel
    {
        public override string Provider => "test-provider";
        public override string Id => "test-voice-model";
    }

    private sealed class FakeTranscriber : IVoiceTranscriber
    {
        public Task<VoiceTranscriptionResult> TranscribeAsync(VoiceTranscriptionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeVoiceGrain(IVoiceTranscriber transcriber) { }
    private sealed class FakeVoiceGrainWithWrongParameterType(string notATranscriber) { }
}
