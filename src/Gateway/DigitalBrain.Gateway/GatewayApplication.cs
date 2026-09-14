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
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace DigitalBrain.Gateway;

internal static class GatewayApplication
{
    // ApplicationStopping never fires during startup, so the one lease read gets a budget of its own: a table
    // endpoint that accepts the connection and then never answers would otherwise keep the gateway from
    // listening at all, because Azure.Core waits about 100 seconds per attempt before it retries.
    private static readonly TimeSpan LeaseReadBudget = TimeSpan.FromSeconds(10);

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
        await AdoptLeasedSlotAsync(app, app.Services.GetRequiredService<SlotRouter>(), clustering).ConfigureAwait(false);

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
                    var interval = slots.MinSwitchInterval.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
                    response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    return Results.Json(
                        new { error = $"the active slot changed less than {interval}s ago", retryAfterSeconds = seconds },
                        statusCode: StatusCodes.Status429TooManyRequests);
                default:
                    return Results.Ok(new { active = slots.Active });
            }
        });
        app.MapGet("/active", static ([FromServices] SlotRouter slots) => Results.Ok(new { active = slots.Active }));
        app.MapReverseProxy();
        return app;
    }

    private static async Task AdoptLeasedSlotAsync(WebApplication app, SlotRouter router, string? clustering)
    {
        if (!string.IsNullOrWhiteSpace(clustering)
            && app.Services.GetKeyedService<TableServiceClient>(DigitalBrainNames.Clustering) is { } tables)
        {
            var stopping = app.Lifetime.ApplicationStopping;
            using var read = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            read.CancelAfter(LeaseReadBudget);
            try
            {
                if (await ActiveSlotRow.ReadOwnerAsync(tables, app.Logger, read.Token).ConfigureAwait(false) is { Length: > 0 } owner)
                {
                    router.Adopt(owner);
                    return;
                }
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                app.Logger.LogWarning(
                    "The active-slot lease row did not answer within {Budget}s; the configured slot stays active.",
                    LeaseReadBudget.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
            }
        }

        app.Logger.LogInformation("No lease row; slot {Slot} from configuration stays active.", router.Active);
    }
}
