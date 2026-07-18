using OpenTelemetry.Trace;
using TripRadar.Server.Infrastructure.Extensions;
using TripRadar.Server.Jobs.API.Extensions;
using TripRadar.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Microsoft.EntityFrameworkCore"))
    .WithTracing(tracing => tracing
        .AddEntityFrameworkCoreInstrumentation()
        .AddHangfireInstrumentation());

builder.Host.ConfigureHostBuilder(builder.Environment);
builder.Services.ConfigureJobsApi(builder.Configuration);
builder.Services.ConfigureInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();
app.ConfigureJobsApp();
app.Run();
