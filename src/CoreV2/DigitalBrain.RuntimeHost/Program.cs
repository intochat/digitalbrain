using Brain.Modules.Conversation;
using Brain.Modules.Proof;
using Brain.Modules.Scheduling;
using Brain.Runtime.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainRuntime();
builder.Services.AddSingleton<IRuntimeProductModule, ConversationProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule, ProofProductModule>();
builder.Services.AddSingleton<IRuntimeProductModule, SchedulingProductModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
