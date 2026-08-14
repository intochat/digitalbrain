using Brain.Modules.Proof;
using Brain.Runtime.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainRuntime();
builder.Services.AddSingleton<IRuntimeProductModule, ProofProductModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
