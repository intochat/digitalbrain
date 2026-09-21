using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.Integration;

public static class IntegrationTest
{
    public static IntegrationTestBuilder Create() => new();
    internal static async Task<IntegrationBrain> StartAsync(
        IReadOnlyList<ModuleDefinition> modules, TestExecutionOptions execution, CancellationToken cancellationToken = default)
        => new(await ModuleTestHost.StartAsync(modules, execution, cancellationToken).ConfigureAwait(false));
}

public sealed class IntegrationBrain : HostedBrain
{
    internal IntegrationBrain(AspireTestSession session) : base(session) { }
}
