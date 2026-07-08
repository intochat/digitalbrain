using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Ino.Context;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Db;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Kernel.Uploads;
using DigitalBrain.Kernel.Voice;
using DigitalBrain.Salesforce;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Ui.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ino = DigitalBrain.Ino;

#pragma warning disable ORLEANSEXP005

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.UseDigitalBrainOrleans();
builder.AddDigitalBrainClients();

builder.ConfigureDigitalBrainKestrel();

#pragma warning restore ORLEANSEXP005

var app = builder.Build();

app.MapDigitalBrainSetup();
app.MapDigitalBrainHandlers();

app.MapDigitalBrainOtlpProxy();

var isAspireHostedForMcp = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__clustering"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__grainstate"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__journal"));

if (!isAspireHostedForMcp)
{
    app.MapMcp().RequireHost("*:8081");
}

if (serveWebBundle)
{
    var indexPath = Path.Combine(Path.GetFullPath(webRoot!), "index.html");
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    });
}

app.Run();
