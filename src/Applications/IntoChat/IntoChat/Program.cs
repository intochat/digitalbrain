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
using IntoChat.ServiceDefaults;
using IntoChat.Workspace;
using Microsoft.AspNetCore.Authentication.Cookies;
using DigitalBrain.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
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

var app = builder.Build();

app.UseKernelCors();
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseAccountSession();
app.MapDefaultEndpoints();
// Developer mode is a server setting, so a client cannot grant itself the C# console.
app.MapGet("/session/capabilities", static (IConfiguration configuration) =>
    Results.Ok(new { developerMode = AgentToolPolicy.DeveloperModeEnabled(configuration["IntoChat:DeveloperMode"]) }));
app.MapPackages();
app.MapDigitalBrainModules();
app.MapGet("/compute/limits", static async (IDigitalBrain brain, CancellationToken ct) =>
    Results.Ok(await brain.Get<IAllowanceLedger>(CallerContextStamper.Require().AccountId).ReadLimitsAsync(ct)));
app.MapWorkspaceDataEndpoints();
app.MapWorkspaceAgent();
app.MapWorkspaceConnections();
app.MapComputeUsage();
app.MapWorkspaceVoice();
app.MapLocalApps();
app.MapBuiltInApps();
app.MapApplications();

app.Run();
