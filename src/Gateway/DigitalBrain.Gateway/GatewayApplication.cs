using System.Globalization;
using Azure.Data.Tables;
using DigitalBrain.Abstractions;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace DigitalBrain.Gateway;

internal static class GatewayApplication
{
    internal static async Task<WebApplication> CreateAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(GatewayOptions.From(builder.Configuration));
        builder.Services.AddSingleton<SlotRouter>();
        builder.Services.AddSingleton<IProxyConfigProvider>(static services => services.GetRequiredService<SlotRouter>().Provider);
        builder.Services.AddReverseProxy();
        var clustering = builder.Configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (!string.IsNullOrWhiteSpace(clustering))
        {
            builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        }

        var app = builder.Build();
        var router = app.Services.GetRequiredService<SlotRouter>();
        if (!string.IsNullOrWhiteSpace(clustering)
            && app.Services.GetKeyedService<TableServiceClient>(DigitalBrainNames.Clustering) is { } tables
            && await ActiveSlotRow.ReadOwnerAsync(tables, app.Logger, app.Lifetime.ApplicationStopping).ConfigureAwait(false) is { Length: > 0 } owner)
        {
            router.Adopt(owner);
        }

        // /health belongs to the active slot, so the gateway maps no health endpoint of its own: a literal
        // route would shadow the kernel's and report the proxy instead of the product.
        // [FromServices] on purpose: a complex parameter on a POST would otherwise be a candidate for the
        // request body.
        app.MapPost("/switch/{slot}", static (string slot, [FromServices] SlotRouter slots, HttpResponse response) =>
        {
            var result = slots.Switch(slot);
            switch (result.Verdict)
            {
                case SwitchVerdict.Unknown:
                    return Results.NotFound(new { error = $"'{slot}' is not a configured slot." });
                case SwitchVerdict.TooSoon:
                    // Answer at once with the wait the caller owes; never hold the request open.
                    var seconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));
                    response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    return Results.Json(
                        new { error = $"the active slot changed less than {slots.MinSwitchInterval.TotalSeconds:0}s ago", retryAfterSeconds = seconds },
                        statusCode: StatusCodes.Status429TooManyRequests);
                default:
                    return Results.Ok(new { active = slots.Active });
            }
        });
        app.MapGet("/active", static ([FromServices] SlotRouter slots) => Results.Ok(new { active = slots.Active }));
        app.MapReverseProxy();
        return app;
    }
}
