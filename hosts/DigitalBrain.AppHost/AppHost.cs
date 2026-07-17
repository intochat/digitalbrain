using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var ollama = builder.AddOllama("ollama")
    .WithGPUSupport()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
ollama.AddModel("llm", "llama3.1:8b");
var ollamaEndpoint = ollama.GetEndpoint("http");

var brainKernel = builder.AddProject<Projects.Brain_Kernel_Host>("brain-kernel")
    .WithEnvironment("Brain__Ai__OllamaEndpoint", ReferenceExpression.Create(
        $"http://{ollamaEndpoint.Property(EndpointProperty.Host)}:{ollamaEndpoint.Property(EndpointProperty.Port)}"));

builder.AddProject<Projects.Brain_Mcp>("brain-mcp").WaitFor(brainKernel);
builder.AddProject<Projects.Brain_UiGateway>("brain-ui").WithHttpEndpoint(port: 5320).WaitFor(brainKernel);

builder.Build().Run();
