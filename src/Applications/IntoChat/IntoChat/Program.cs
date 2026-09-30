using DigitalBrain.Core;
using DigitalBrain.AI.Agents;
using DigitalBrain.Aspire;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk;
using IntoChat;
using IntoChat.Marketplace;
using IntoChat.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using DigitalBrain.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIntoChatOptions();
builder.AddServiceDefaults();
builder.AddDigitalBrain();
builder.AddMarketplace();
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
    Results.Ok(new { developerMode = DeveloperMode.IsEnabled(configuration) }));
app.MapDigitalBrainModules();
app.MapMarketplace();

app.Run();
