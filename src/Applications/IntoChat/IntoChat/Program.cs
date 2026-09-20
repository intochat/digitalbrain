using DigitalBrain.Aspire;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.Workspace;
using IntoChat.ServiceDefaults;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddBehaviors();
builder.AddKernelCors();
builder.Services.AddAuthentication();

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

app.Run();
