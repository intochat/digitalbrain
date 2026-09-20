using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.E2E;

public static class E2EDigitalBrainSimulation
{
    public static Task<E2EBrain> StartAsync<TAppHost>(IApplicationConfiguration options, CancellationToken cancellationToken = default)
        where TAppHost : class => StartAsync<TAppHost>(options, new(), cancellationToken);

    public static async Task<E2EBrain> StartAsync<TAppHost>(IApplicationConfiguration options, TestExecutionOptions execution,
        CancellationToken cancellationToken = default) where TAppHost : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = options.CreateSnapshot();
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = [];
        foreach (var (key, value) in snapshot.Configuration)
        {
            if (key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("Application options cannot override test-owned connections or identity.", nameof(options)); }
            args.Add($"{key}={value}");
        }
        args.Add("DigitalBrain:Testing:Enabled=true");
        args.Add($"Orleans:ClusterId={identity}");
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return new(session);
    }
}
