using DigitalBrain;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Assistant;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.OS;
using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// IntoChat is a distribution of the operating system: the composition is the OS's, and what
// is IntoChat's own is branding, shipped apps and configuration.
builder.AddDigitalBrainServer(server => server.UseAzureStorage().WithDashboard().AddOperatingSystem());
builder.Services.AddOperatingSystemBoot(os => os.Apps.Add("intochat/settings"));
builder.AddDigitalBrainPlatform();
builder.Services.Configure<AssistantOptions>(options =>
{
    options.DisplayName = "IntoChat assistant";
    options.Instructions = "You are the IntoChat workspace assistant. Customer Researcher stores results in Postgres public.customer_research; filter its workspace column to the current workspace id.";
});
builder.Services.Configure<DigitalBrain.Flutter.FlutterModuleOptions>(options => options.AssistantTitle = "IntoChat");
builder.Services.AddShippedApps(typeof(Program).Assembly, "IntoChat.ShippedApps/", publisher: "intochat");
builder.Services.Configure<BuiltInSettingsOptions>(options => options.Package = PackageId.Create("intochat", "settings"));

var app = builder.Build();

app.MapDefaultEndpoints();
app.UsePlatformHttp();
app.MapDigitalBrainModules();
app.MapDigitalBrainPlatform();
app.MapDigitalBrainDashboard();


app.Run();
