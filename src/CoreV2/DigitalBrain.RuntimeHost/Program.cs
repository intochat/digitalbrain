using Brain.Modules.Behavior;
using Brain.Modules.Conversation;
using Brain.Core.Journaling;
using Brain.Modules.Memory;
using Brain.Modules.Proof;
using Brain.Modules.Scheduling;
using Brain.Runtime.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainRuntime(silo =>
{
#pragma warning disable ORLEANSEXP005 // CoreV2 intentionally uses the Orleans durable journal preview.
    silo.UseJsonJournalFormat(CoreJournalJsonContext.Default);
#pragma warning restore ORLEANSEXP005
});
builder.Services.AddSingleton<IRuntimeProductModule, ProofProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule, ConversationProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule, SchedulingProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule, BehaviorProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
    "ai",
    "AI",
    "Configure a local or hosted model provider."));
builder.Services.AddSingleton<IRuntimeProductModule, MemoryProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
    "google",
    "Google",
    "Configure the Google MCP connection."));
builder.Services.AddSingleton<IRuntimeProductModule>(new SetupRequiredProductModule(
    "salesforce",
    "Salesforce",
    "Configure the Salesforce MCP connection."));

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
