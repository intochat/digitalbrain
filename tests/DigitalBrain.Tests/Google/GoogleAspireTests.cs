using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.Google;
using Xunit;

namespace DigitalBrain.Tests.Google;

public sealed class GoogleAspireTests
{
    [Fact]
    public async Task Google_extension_declares_secret_parameters_and_kernel_environment()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            AssemblyName = typeof(GoogleAspireTests).Assembly.GetName().Name,
            DisableDashboard = true
        });
        var kernel = builder.AddContainer("kernel", "busybox").WithDigitalBrainGoogle();

        var parameters = builder.Resources
            .OfType<ParameterResource>()
            .ToDictionary(resource => resource.Name);
        Assert.True(parameters["google-client-id"].Secret);
        Assert.True(parameters["google-client-secret"].Secret);
        Assert.False(parameters["google-redirect-uri"].Secret);

        Assert.True(kernel.Resource.TryGetEnvironmentVariables(out var callbacks));
        var environment = new Dictionary<string, object>();
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, kernel.Resource, environment, CancellationToken.None);
        foreach (var callback in callbacks)
            await callback.Callback(context);

        Assert.Equal(
            [
                "DigitalBrain__Google__ClientId",
                "DigitalBrain__Google__ClientSecret",
                "DigitalBrain__Google__RedirectUri"
            ],
            environment.Keys.Order(StringComparer.Ordinal).ToArray());
    }
}
