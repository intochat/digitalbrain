#!/usr/bin/env dotnet
#:project ../Modules/DigitalBrain/DigitalBrain/DigitalBrain.csproj
#:project ../Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:include TimerReport.cs
#:property PublishAot=false

using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleansClient(client =>
{
    client.AddDigitalBrain();
    var gateways = builder.Configuration["Gateways"];
    if (string.IsNullOrWhiteSpace(gateways))
    {
        // Local development only. Deployed clients supply explicit gateways and IDs.
        client.UseLocalhostClustering();
    }
    else
    {
        client.UseStaticClustering(options => options.Gateways = gateways.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => new Uri(value)).ToList());
        client.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = builder.Configuration["ClusterId"] ?? throw new InvalidOperationException("ClusterId is required.");
            options.ServiceId = builder.Configuration["ServiceId"] ?? throw new InvalidOperationException("ServiceId is required.");
        });
    }
});
using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TimerReport");
using var stopping = CancellationTokenSource.CreateLinkedTokenSource(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
var smoke = builder.Configuration.GetValue<bool>("Smoke");
var timerId = builder.Configuration["TimerId"] ?? "tea";
try
{
    await host.StartAsync(stopping.Token);
    await TimerReport.RunAsync(host.Services.GetRequiredService<IDigitalBrain>(), timerId, fact =>
    {
        Console.WriteLine($"TimerTick {fact.TimerId} {fact.ObservedAt:O}");
        if (smoke) { stopping.Cancel(); }
        return Task.CompletedTask;
    }, stopping.Token);
    return 0;
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return 0; }
catch (Exception error)
{
    logger.LogError(error, "Timer behavior failed");
    return 1;
}
finally
{
    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await host.StopAsync(shutdown.Token);
}
