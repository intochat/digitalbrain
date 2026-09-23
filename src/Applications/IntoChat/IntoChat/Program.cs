using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire;
using DigitalBrain.Automations;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.Agent;
using IntoChat.Apps;
using IntoChat.LocalFiles;
using IntoChat.Operations;
using IntoChat.ServiceDefaults;
using IntoChat.Workspace;
using Microsoft.AspNetCore.Authentication.Cookies;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddBehaviors();
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
builder.Services.AddDataProtection();
builder.Services.Configure<LocalFilesOptions>(builder.Configuration.GetSection("IntoChat:LocalFiles"));
builder.Services.AddSingleton<LocalFileStore>();
builder.Services.AddSingleton<AppSurfaceComposer>();
builder.Services.AddSingleton<ImageSaveCoordinator>();
builder.Services.AddSingleton<LiveTableWindows>();
builder.Services.AddSingleton<IAgentToolFactory, WorkspaceTableTools>();
builder.Services.AddSingleton<IAgentToolFactory, WorkspaceFormTools>();
builder.Services.AddSingleton<IAgentToolFactory, DiscoveryTools>();
builder.Services.AddSingleton<IAutomationActionCatalog, FirstPartyAutomationActionCatalog>();
builder.Services.AddSingleton<IAutomationActionInvoker, LeadGeneratorAutomationInvoker>();
builder.Services.AddSingleton<IProblemReportStore, ProblemReportStore>();
builder.Services.AddSingleton<IWorkspaceVectorPurge, MemoryWorkspaceVectorPurge>();
builder.Services.AddSingleton<IWorkspaceBackupPurge, HostedWorkspaceBackupPurge>();
builder.Services.AddSingleton<WorkspaceDeletion>();

var app = builder.Build();

app.UseKernelCors();
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseAccountSession();
app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans");
app.MapBehaviors();
app.MapDigitalBrainModules();
app.MapWorkspaceDataEndpoints();
app.MapWorkspaceAgent();
app.MapLocalApps();

app.Run();