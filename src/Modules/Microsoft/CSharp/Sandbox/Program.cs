using DigitalBrain.Microsoft.CSharp.Sandbox;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddOptions<SandboxOptions>().BindConfiguration(SandboxOptions.SectionName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IScriptLauncher, DotNetScriptLauncher>();
builder.Services.AddSingleton<SandboxRuns>();

var app = builder.Build();
app.MapSandbox();
await app.RunAsync();
