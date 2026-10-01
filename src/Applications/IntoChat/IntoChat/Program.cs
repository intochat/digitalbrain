using DigitalBrain.Core;
using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk;
using IntoChat;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using IntoChat.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using DigitalBrain.Identity;
using DigitalBrain.Platform.Secrets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrainRuntime();
builder.Services.Configure<AssistantOptions>(options =>
{
    options.DisplayName = "IntoChat assistant";
    options.Instructions = "You are the IntoChat workspace assistant. Customer Researcher stores results in Postgres public.customer_research; filter its workspace column to the current workspace id.";
});
builder.Services.Configure<DigitalBrain.Flutter.FlutterModuleOptions>(options => options.AssistantTitle = "IntoChat");
builder.Services.AddShippedApps(typeof(Program).Assembly, "IntoChat.ShippedApps/", publisher: "intochat");
builder.Services.Configure<BuiltInSettingsOptions>(options => options.Package = PackageId.Create("intochat", "settings"));
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
builder.Services.AddMasterKeyWrapper();
builder.Services.AddCookieProtection();

var app = builder.Build();

app.UseKernelCors();
app.UseModuleHttpSurfaces();
app.UseAuthentication();
app.UseAccountSession();
app.MapDefaultEndpoints();
app.MapGet("/session/capabilities", static (IServiceProvider services) =>
    Results.Ok(new { developerMode = services.GetService<DigitalBrain.Apps.IScriptSandbox>()?.CanRun == true }));
app.MapDigitalBrainModules();


app.Run();

