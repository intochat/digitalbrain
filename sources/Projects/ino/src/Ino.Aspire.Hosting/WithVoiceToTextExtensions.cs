using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public static class WithVoiceToTextExtensions
{
    public static IInoBuilder WithVoiceToText<TProvider>(this IInoBuilder builder)
        where TProvider : VoiceToTextProvider, new()
    {
        builder.RegisterVoiceProvider(new TProvider());
        return builder;
    }
}
