using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public static class BrainTestMode
{
    public static IResourceBuilder<T> WithBrainTestMode<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEnvironment("DigitalBrain__Mode", DigitalBrainNames.TestingMode);
    }
}
