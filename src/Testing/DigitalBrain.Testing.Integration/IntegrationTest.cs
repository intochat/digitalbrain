using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.Integration;

public static class IntegrationTest
{
    public static IntegrationTestBuilder Create() => new();
    internal static async Task<IntegrationBrain> StartAsync(IntegrationOptions options, CancellationToken cancellationToken = default)
    {
        return new(await ModuleTestHost.StartAsync(options.Modules, options.Execution, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class IntegrationBrain : HostedBrain
{
    internal IntegrationBrain(AspireTestSession session) : base(session) { }
    public Task RestartRuntimeAsync(CancellationToken cancellationToken = default) => Session.RestartRuntimeAsync(cancellationToken);
}
