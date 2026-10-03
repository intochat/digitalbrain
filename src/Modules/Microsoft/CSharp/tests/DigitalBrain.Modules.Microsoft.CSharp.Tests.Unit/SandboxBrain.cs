using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

// A unit brain with the C# module running against a fake sandbox. In-process brains have no HTTP
// listener, so the script edge address is configured rather than derived.
internal static class SandboxBrain
{
    public static Task<UnitBrain> StartAsync(FakeSandbox sandbox, CancellationToken ct, Action<ISiloBuilder>? configure = null)
        => UnitTest.Create().WithModule<CSharpModule>().WithReminders()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddHttpClient<ICSharpRunner, AspireSandboxRunner>().ConfigurePrimaryHttpMessageHandler(() => sandbox);
                silo.Services.PostConfigure<CSharpDeploymentSettings>(options => options.EdgeUrl = "http://edge.test/");
                configure?.Invoke(silo);
            })
            .StartAsync(ct);
}
