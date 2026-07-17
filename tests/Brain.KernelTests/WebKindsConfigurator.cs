using Brain.Kernel;
using Brain.Modules.Web;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace Brain.KernelTests;

public sealed class WebKindsConfigurator : ISiloConfigurator
{
    public static StubHttpHandler Handler { get; } = new();

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.Services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => Handler);
        siloBuilder.AddBrainKind("web", sp => new WebKind(sp.GetRequiredService<IHttpClientFactory>()));
    }
}
