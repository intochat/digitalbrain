using Aspire.Hosting;
using DigitalBrain;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "The DigitalBrain quickstart AppHost is disabled outside Development.");

var owner = builder.AddParameter("digitalbrain-owner");
var brain = builder.AddDigitalBrain("brain")
    .WithLLM<GptFast>().AsFast()
    .WithLLM<ClaudeBalanced>().AsBalanced()
    .WithLLM<GptReasoning>().AsReasoning()
    .WithEmbedding<TextEmbedding>();
var kernel = builder
    .AddProject<Projects.DigitalBrain_Quickstart_Kernel>("kernel")
    .WithReference(brain);
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
        .WaitFor(kernel);
    builder
        .AddProject<Projects.DigitalBrain_Quickstart_DevUI>("devui")
        .WithReference(client)
        .WithEnvironment("DigitalBrain__DevTools__Owner", owner)
        .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
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
    return;
}

builder.Build().Run();
