using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

// The silo refuses to start without a declared DigitalBrain:Auth:Posture. A local run defaults
// to the Open development posture; a publish carries no default, so every deployment states its
// posture itself. The AppHost's own configuration overrides the run-mode default.
internal static class AuthPosture
{
    private const string Key = "DigitalBrain:Auth:Posture";

    public static DigitalBrainModuleProjection? Provision(IDistributedApplicationBuilder builder)
    {
        var posture = builder.Configuration[Key] ?? (builder.ExecutionContext.IsRunMode ? "Open" : null);
        return posture is null ? null : new Projection(posture);
    }

    private sealed class Projection(string posture) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
            => builder.WithEnvironment(Key.Replace(":", "__", StringComparison.Ordinal), posture);
    }
}
