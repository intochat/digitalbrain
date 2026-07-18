using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Brain.Modules.Ai;

public static class AiAspireExtensions
{
    public static IResourceBuilder<T> WithDigitalBrainAI<T>(
        this IResourceBuilder<T> kernel)
        where T : IResourceWithEnvironment
    {
        var ollama = kernel.ApplicationBuilder.AddOllama("ollama")
            .WithGPUSupport()
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
        ollama.AddModel("llm", "llama3.1:8b");
        var endpoint = ollama.GetEndpoint("http");

        return kernel.WithEnvironment(
            "Brain__Ai__OllamaEndpoint",
            ReferenceExpression.Create(
                $"http://{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}"));
    }
}
