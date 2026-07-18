using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.DevTools;

public static class DigitalBrainDevUIExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainDevUI(
        this IHostApplicationBuilder builder,
        string name,
        Action<DigitalBrainDevUIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = new DigitalBrainDevUIOptions();
        configure?.Invoke(options);
        var authToken = ResolveAuthToken(options.AuthToken);
        ValidateEnvironment(builder.Environment, options, authToken);
        var owner = ResolveOwner(builder, options);
        if (options.AllowRemoteAccess &&
            string.IsNullOrWhiteSpace(authToken))
            throw new InvalidOperationException(
                "Remote DigitalBrain DevUI access requires an authentication token.");

        builder.AddDigitalBrainClient(name);
        builder.AddDevUI(devUI =>
        {
            devUI.AllowRemoteAccess = options.AllowRemoteAccess;
            devUI.AuthToken = authToken;
        });
        builder.AddOpenAIResponses();
        builder.AddOpenAIConversations();
        builder.Services.AddSingleton(serviceProvider =>
            new DigitalBrainDevUIOwnerBinding(
                serviceProvider.GetRequiredService<DigitalBrainSessionFactory>(),
                owner));
        AddAgent(builder.Services, DigitalBrainDevUIAgentNames.Fast, ConversationRole.Fast);
        AddAgent(
            builder.Services,
            DigitalBrainDevUIAgentNames.Balanced,
            ConversationRole.Balanced);
        AddAgent(
            builder.Services,
            DigitalBrainDevUIAgentNames.Reasoning,
            ConversationRole.Reasoning);
        builder.Services.AddSingleton(new DigitalBrainDevUIRegistration(options, authToken));
        return builder;
    }

    public static IEndpointRouteBuilder MapDigitalBrainDevUI(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registration = endpoints.ServiceProvider
            .GetRequiredService<DigitalBrainDevUIRegistration>();
        ApplyAccessControl(endpoints.MapDevUI(), registration);
        ApplyAccessControl(endpoints.MapOpenAIResponses(), registration);
        ApplyAccessControl(endpoints.MapOpenAIConversations(), registration);
        return endpoints;
    }

    private static void AddAgent(
        IServiceCollection services,
        string name,
        ConversationRole role)
    {
        services.AddAIAgent(
            name,
            (serviceProvider, agentName) =>
            {
                var chatClient = serviceProvider
                    .GetRequiredService<DigitalBrainDevUIOwnerBinding>()
                    .CreateClient(role);
                return chatClient.AsAIAgent(
                    name: agentName,
                    description: $"Owner-bound DigitalBrain {agentName} conversation agent.");
            },
            ServiceLifetime.Singleton);
    }

    private static BrainOwnerId ResolveOwner(
        IHostApplicationBuilder builder,
        DigitalBrainDevUIOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OwnerConfigurationKey))
            throw new InvalidOperationException(
                "DigitalBrain DevUI requires an owner configuration key.");
        var ownerValue = builder.Configuration[options.OwnerConfigurationKey];
        if (string.IsNullOrWhiteSpace(ownerValue))
            throw new InvalidOperationException(
                "DigitalBrain DevUI requires an explicit owner.");

        var owner = new BrainOwnerId(ownerValue);
        _ = ConversationKey.Encode(owner, new ConversationId("devui-owner-validation"));
        return owner;
    }

    private static void ValidateEnvironment(
        IHostEnvironment environment,
        DigitalBrainDevUIOptions options,
        string? authToken)
    {
        if (!environment.IsDevelopment() && !options.AllowProduction)
            throw new InvalidOperationException(
                "DigitalBrain DevUI is disabled outside Development unless production access is explicitly enabled.");
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(authToken))
            throw new InvalidOperationException(
                "Production DigitalBrain DevUI access requires an authentication token.");
    }

    private static void ApplyAccessControl(
        IEndpointConventionBuilder mapped,
        DigitalBrainDevUIRegistration registration)
    {
        mapped.AddEndpointFilter(new DigitalBrainDevelopmentAccessFilter(
            registration.Options.AllowRemoteAccess,
            registration.AuthToken));
        mapped.WithMetadata(new DigitalBrainDevelopmentAccessMetadata(
            !registration.Options.AllowRemoteAccess,
            !string.IsNullOrWhiteSpace(registration.AuthToken)));
        registration.Options.ConfigureEndpoints?.Invoke(mapped);
    }

    private static string? ResolveAuthToken(string? configuredToken) =>
        string.IsNullOrWhiteSpace(configuredToken)
            ? Environment.GetEnvironmentVariable(DevUIOptions.AuthTokenEnvironmentVariable)
            : configuredToken;

    private sealed record DigitalBrainDevUIRegistration(
        DigitalBrainDevUIOptions Options,
        string? AuthToken);
}
