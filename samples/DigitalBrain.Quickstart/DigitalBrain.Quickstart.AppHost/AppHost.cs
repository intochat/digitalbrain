using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "The DigitalBrain quickstart AppHost is disabled outside Development.");

var live = string.Equals(
    builder.Configuration["DigitalBrain:Quickstart:Live"],
    "true",
    StringComparison.OrdinalIgnoreCase);
var controlledEndpoint = live
    ? ControlledProviderConfiguration.Endpoint
    : null;
var providerPort = live
    ? ControlledProviderConfiguration.RequirePort(
        builder.Configuration["DigitalBrain:Quickstart:ProviderPort"],
        controlledEndpoint!.Port,
        "provider")
    : 0;
var driverPort = live
    ? ControlledProviderConfiguration.RequirePort(
        builder.Configuration["DigitalBrain:Quickstart:DriverPort"],
        fallback: 0,
        "driver")
    : 0;
if (live && providerPort != controlledEndpoint!.Port)
    throw new InvalidOperationException(
        "The live provider port must match the controlled provider endpoint.");
var owner = builder.AddParameter("digitalbrain-owner");
var brain = live
    ? builder.AddDigitalBrain("brain")
        .WithLLM<ControlledGptFast>().AsFast()
        .WithLLM<ControlledClaudeBalanced>().AsBalanced()
        .WithLLM<ControlledGptReasoning>().AsReasoning()
        .WithEmbedding<ControlledTextEmbedding>()
    : builder.AddDigitalBrain("brain")
        .WithLLM<GptFast>().AsFast()
        .WithLLM<ClaudeBalanced>().AsBalanced()
        .WithLLM<GptReasoning>().AsReasoning()
        .WithEmbedding<TextEmbedding>();
var openAIKey = live
    ? ControlledProviderConfiguration.RequireParameter(
        builder,
        "brain-openai-openai-apikey")
    : null;
var anthropicKey = live
    ? ControlledProviderConfiguration.RequireParameter(
        builder,
        "brain-anthropic-api-key")
    : null;
var testProvider = live
    ? builder
        .AddProject<Projects.DigitalBrain_Quickstart_TestProvider>(
            "test-provider")
        .WithEnvironment(
            "ASPNETCORE_URLS",
            controlledEndpoint!.GetLeftPart(UriPartial.Authority))
        .WithEnvironment(
            "DigitalBrain__Quickstart__OpenAISecret",
            openAIKey!)
        .WithEnvironment(
            "DigitalBrain__Quickstart__AnthropicSecret",
            anthropicKey!)
        .WithEnvironment("DOTNET_ENVIRONMENT", Environments.Development)
        .WithHttpEndpoint(
            port: providerPort,
            targetPort: providerPort,
            name: "http",
            isProxied: false)
        .WithHttpHealthCheck("/health")
    : null;
var kernel = builder
    .AddProject<Projects.DigitalBrain_Quickstart_Kernel>("kernel")
    .WithReference(brain);
if (testProvider is not null)
    kernel.WaitFor(testProvider);
var client = brain.AsClient();

builder
    .AddProject<Projects.DigitalBrain_Quickstart_Console>("console")
    .WithReference(client)
    .WithEnvironment("DigitalBrain__DevTools__Owner", owner)
    .WithEnvironment("DOTNET_ENVIRONMENT", Environments.Development)
    .WaitFor(kernel)
    .WithArgs("--environment-probe")
    .WithExplicitStart();
if (builder.Environment.IsDevelopment())
{
    builder
        .AddProject<Projects.DigitalBrain_Quickstart_OrleansDashboard>(
            "orleans-dashboard")
        .WithReference(client)
        .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
        .WithHttpHealthCheck("/health")
        .WaitFor(kernel);
    builder
        .AddProject<Projects.DigitalBrain_Quickstart_DevUI>("devui")
        .WithReference(client)
        .WithEnvironment("DigitalBrain__DevTools__Owner", owner)
        .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
        .WithHttpHealthCheck("/health")
        .WaitFor(kernel);
}
if (live)
{
    builder
        .AddProject<Projects.DigitalBrain_Quickstart_Console>(
            "console-test-driver")
        .WithReference(client)
        .WithEnvironment("DigitalBrain__DevTools__Owner", owner)
        .WithEnvironment("DOTNET_ENVIRONMENT", Environments.Development)
        .WithEnvironment(
            "ASPNETCORE_URLS",
            FormattableString.Invariant($"http://127.0.0.1:{driverPort}"))
        .WithArgs("--live-driver")
        .WithHttpEndpoint(
            port: driverPort,
            targetPort: driverPort,
            name: "http",
            isProxied: false)
        .WithHttpHealthCheck("/health")
        .WaitFor(kernel);
}

if (args.Contains("--model-contract", StringComparer.Ordinal))
{
    foreach (var resource in builder.Resources.OrderBy(
                 resource => resource.Name,
                 StringComparer.Ordinal))
        Console.WriteLine($"resource:{resource.Name}");
    Console.WriteLine($"resource:{brain.Resource.Orleans.Name}");
    Console.WriteLine($"resource:{brain.Resource.ClientOrleans.Name}");
    if (live)
    {
        Console.WriteLine(
            $"endpoint:openai:{ControlledProviderConfiguration.OpenAIEndpoint}");
        Console.WriteLine(
            $"endpoint:anthropic:{ControlledProviderConfiguration.Endpoint}");
    }
    return;
}

builder.Build().Run();

internal static class ControlledProviderConfiguration
{
    public static Uri Endpoint
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(
                "DigitalBrain__Quickstart__ProviderEndpoint");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttp ||
                !endpoint.IsLoopback ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                endpoint.AbsolutePath != "/")
                throw new InvalidOperationException(
                    "The live controlled provider requires an absolute loopback HTTP endpoint.");
            return endpoint;
        }
    }

    public static Uri OpenAIEndpoint => new(Endpoint, "/v1");

    public static int RequirePort(
        string? value,
        int fallback,
        string resource)
    {
        var port = int.TryParse(value, out var configured)
            ? configured
            : fallback;
        if (port is < 1 or > 65535)
            throw new InvalidOperationException(
                $"The live {resource} port is invalid.");
        return port;
    }

    public static IResourceBuilder<ParameterResource> RequireParameter(
        IDistributedApplicationBuilder builder,
        string name)
    {
        var resource = builder.Resources
            .OfType<ParameterResource>()
            .SingleOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (resource is null)
            throw new InvalidOperationException(
                $"The live provider credential parameter '{name}' is missing.");
        return builder.CreateResourceBuilder(resource);
    }
}

internal sealed record ControlledGptFast : ChatModelDescriptor
{
    public ControlledGptFast()
        : base(ModelProvider.OpenAI, "gpt-5-mini")
    {
        Endpoint = ControlledProviderConfiguration.OpenAIEndpoint;
    }
}

internal sealed record ControlledClaudeBalanced : ChatModelDescriptor
{
    public ControlledClaudeBalanced()
        : base(ModelProvider.Anthropic, "claude-sonnet-4-5")
    {
        Endpoint = ControlledProviderConfiguration.Endpoint;
    }
}

internal sealed record ControlledGptReasoning : ChatModelDescriptor
{
    public ControlledGptReasoning()
        : base(ModelProvider.OpenAI, "gpt-5")
    {
        Endpoint = ControlledProviderConfiguration.OpenAIEndpoint;
    }
}

internal sealed record ControlledTextEmbedding : EmbeddingModelDescriptor
{
    public ControlledTextEmbedding()
        : base(ModelProvider.OpenAI, "text-embedding-3-small")
    {
        Endpoint = ControlledProviderConfiguration.OpenAIEndpoint;
    }
}
