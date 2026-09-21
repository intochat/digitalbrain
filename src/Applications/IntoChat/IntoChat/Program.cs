using IntoChat.Workspace.Queries;
using DigitalBrain.Aspire;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.Workspace;
using IntoChat.Agent;
using DigitalBrain.AI.Agents;
using IntoChat.ServiceDefaults;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddBehaviors();
builder.AddKernelCors();
builder.Services.AddAuthentication();
builder.Services.AddSingleton<QueryWindowOperation>();
builder.Services.AddSingleton<IAgentToolFactory, SupabaseWorkspaceTools>();
builder.Services.AddSingleton<ConversationCoordinator>();

var app = builder.Build();

app.UseKernelCors();
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseBasicAuthGate();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapBehaviors();
app.MapDigitalBrainModules();
app.MapWorkspaceDataEndpoints();
app.MapWorkspaceAgent();

app.Run();
