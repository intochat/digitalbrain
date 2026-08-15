using Brain.Modules.AI;
using Brain.Core.Journaling;
using Brain.Modules.Proof;
using Brain.Modules.UI;
using Brain.Core.Runtime;
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
builder.Services.AddSingleton<IBrainOperationHandler, ProofWireOperationHandler>();
builder.Services.AddSingleton<IBrainOperationHandler, ProofRunOperationHandler>();
builder.Services.AddCoreV2AI(builder.Configuration);
builder.Services.AddSingleton<IBrainOperationHandler, ChatSendOperationHandler>();
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
