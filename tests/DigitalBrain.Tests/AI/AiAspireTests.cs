using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.Ai;
using Xunit;

namespace DigitalBrain.Tests.AI;

public sealed class AiAspireTests
{
    [Fact]
    public async Task Ai_extension_owns_ollama_model_and_kernel_endpoint_wiring()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            AssemblyName = typeof(AiAspireTests).Assembly.GetName().Name,
            DisableDashboard = true
        });
        var kernel = builder.AddContainer("kernel", "busybox").WithDigitalBrainAI();

        Assert.Contains(builder.Resources, resource => resource.Name == "ollama");
        Assert.Contains(builder.Resources, resource => resource.Name == "llm");
        Assert.True(kernel.Resource.TryGetEnvironmentVariables(out var callbacks));

        var environment = new Dictionary<string, object>();
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, kernel.Resource, environment, CancellationToken.None);
        foreach (var callback in callbacks)
            await callback.Callback(context);

        Assert.Contains("Brain__Ai__OllamaEndpoint", environment.Keys);
    }

    [Fact]
    public void App_host_contains_no_ai_provider_details()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        Assert.DoesNotContain("AddOllama", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("llama3.1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Brain__Ai__OllamaEndpoint", source, StringComparison.Ordinal);
        Assert.Contains("WithDigitalBrainAI", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
