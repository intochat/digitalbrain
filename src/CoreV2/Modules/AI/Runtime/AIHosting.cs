using Brain.Core.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace Brain.Modules.AI;

public static class AIHosting
{
    public const string EndpointConfigurationKey = "DigitalBrain:AI:Ollama:Endpoint";
    public const string ModelConfigurationKey = "DigitalBrain:AI:Ollama:Model";

    public static IServiceCollection AddCoreV2AI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IChatClient>(_ => CreateClient(configuration));
        services.AddSingleton<IAssistantChatModel, OllamaAssistantChatModel>();
        services.AddSingleton<IBrainOperationHandler, AssistantChatOperationHandler>();
        return services;
    }

    private static IChatClient CreateClient(IConfiguration configuration)
    {
        var endpoint = configuration[EndpointConfigurationKey];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri is null
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{EndpointConfigurationKey} must be an absolute HTTP(S) URI supplied by Aspire.");
        }
        var model = configuration[ModelConfigurationKey] ?? "gemma4:12b";
        var http = new HttpClient
        {
            BaseAddress = uri,
            Timeout = TimeSpan.FromMinutes(10),
        };
        return new ChatClientBuilder(new OllamaApiClient(http, model)).Build();
    }
}
