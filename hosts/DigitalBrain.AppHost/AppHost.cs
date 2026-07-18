using Aspire.Hosting;
using DigitalBrain;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain")
    .WithLLM<GptFast>().AsFast()
    .WithLLM<ClaudeBalanced>().AsBalanced()
    .WithLLM<GptReasoning>().AsReasoning()
    .WithEmbedding<TextEmbedding>();

var kernel = builder.AddProject<Projects.Brain_Kernel_Host>("kernel")
    .WithReference(brain);

builder.AddContainer(
        "restricted-client",
        "mcr.microsoft.com/dotnet/runtime",
        "8.0")
    .WithReference(brain.AsClient())
    .WaitFor(kernel)
    .WithExplicitStart();

builder.Build().Run();
