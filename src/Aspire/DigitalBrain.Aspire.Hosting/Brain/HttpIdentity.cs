using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed record HttpIdentity(string CookieName, string ProtectionApplicationName, string ProtectionContainerName)
{
    internal void Apply<T>(IResourceBuilder<T> resource, DigitalBrainBuilder brain)
        where T : IResourceWithEnvironment
    {
        foreach (var (key, fallback) in new[]
        {
            (nameof(CookieName), CookieName),
            (nameof(ProtectionApplicationName), ProtectionApplicationName),
            (nameof(ProtectionContainerName), ProtectionContainerName),
        })
        {
            var value = brain.ApplicationBuilder.Configuration["DigitalBrain:Identity:" + key] ?? fallback;
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            resource.WithEnvironment("DigitalBrain__Identity__" + key, value);
        }
    }
}
