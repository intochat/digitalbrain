using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Gmail;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrain();
builder.Services.AddSalesforceBrowserAuthorization(builder.Configuration);
builder.Services.AddGmailBrowserAuthorization(builder.Configuration);
builder.AddKernelCors();
builder.Services.TryAddSingleton(static services =>
    new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

var app = builder.Build();
app.UseKernelCors();
app.UseSalesforceBrowserAuthorization();
app.UseGmailBrowserAuthorization();
app.UseAuthentication();
app.UseBasicAuthGate();
app.MapDefaultEndpoints();
app.MapOwnerCommands();
app.MapChatVoice();
app.MapChatStreams();
app.MapKitEntities();
app.MapBehaviors();
app.MapSurfaceStreams();
app.MapOrleansDashboard("/orleans");
app.Run();
