using IntoChat.Packages;
using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.Agent;
using IntoChat.Apps;
using IntoChat.Applications;
using IntoChat.LocalFiles;
using IntoChat.ServiceDefaults;
using IntoChat.Workspace;
using Microsoft.AspNetCore.Authentication.Cookies;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddCSharp();
builder.AddPackages();
builder.AddKernelCors();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "intochat.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.AddDurableProtection();
builder.Services.Configure<LocalFilesOptions>(builder.Configuration.GetSection("IntoChat:LocalFiles"));
builder.Services.AddSingleton<IAssetBlobStore, AzureAssetBlobStore>();
builder.Services.AddSingleton<LocalFileStore>();
builder.Services.AddSingleton<AppSurfaceComposer>();
builder.Services.AddSingleton<ImageSaveCoordinator>();
builder.Services.AddSingleton<LiveTableWindows>();
builder.Services.AddApplications();
builder.Services.AddSingleton<IAgentToolFactory, WorkspaceTableTools>();
builder.Services.AddSingleton<IAgentToolFactory, WorkspaceFormTools>();
builder.Services.AddSingleton<IAgentToolFactory, DiscoveryTools>();
builder.Services.AddSingleton<IAgentContextProvider, CapabilityContextProvider>();

var app = builder.Build();

app.UseKernelCors();
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseAccountSession();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapCSharp();
app.MapPackages();
app.MapDigitalBrainModules();
app.MapGet("/compute/limits", static async (IDigitalBrain brain, CancellationToken ct) =>
    Results.Ok(await brain.Get<IAllowanceLedger>(CallerContextStamper.Require().AccountId).ReadLimitsAsync(ct)));
app.MapWorkspaceDataEndpoints();
app.MapShellPersistence();
app.MapWorkspaceAgent();
app.MapWorkspaceConnections();
app.MapComputeUsage();
app.MapWorkspaceVoice();
app.MapLocalApps();
app.MapBuiltInApps();
app.MapApplications();

app.Run();
