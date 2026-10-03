using DigitalBrain.Kernel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire.Server;
using DigitalBrain.Compute;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk;
using IntoChat;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using IntoChat.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDigitalBrainRuntime();
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
app.MapDigitalBrainModules();


app.Run();
