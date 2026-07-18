using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);

DigitalBrainKernelBootstrapper.ConfigureServices(builder);

var app = builder.Build();

DigitalBrainKernelBootstrapper.ConfigurePipeline(app);

await app.RunAsync();

public partial class Program { }

