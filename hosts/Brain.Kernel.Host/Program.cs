using Brain.Kernel;
using Brain.Modules.Ai;
using Brain.Modules.Behaviors;
using Brain.Modules.Google;
using Brain.Modules.Web;
using Brain.Modules.Workspace;
using DigitalBrain.ServiceDefaults;
using Orleans.Journaling;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddJournalStorage();
    silo.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
    silo.AddBrainKernel(new ChatKind(), new WindowKind(), new FeedKind());
    silo.AddDigitalBrainAI(builder.Configuration);
    silo.AddBrainWeb();
    silo.AddDigitalBrainGoogle(builder.Configuration, builder.Environment);
    silo.AddBrainBehaviors();
});
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/oauth/callback/google", async (
    string? code,
    string? state,
    string? error,
    IClusterClient clusterClient) =>
{
    const string uiRedirect = "http://localhost:5320/";
    string status;

    if (string.Equals(error, "access_denied", StringComparison.Ordinal))
    {
        status = "consent-denied";
    }
    else if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
    {
        status = "invalid-request";
    }
    else
    {
        try
        {
            const string connectionKey = "local-owner|actor/ui-dev|connection/google-primary";
            const string callerKey = "local-owner|actor/ui-dev|session/dev";
            var connection = clusterClient.GetGrain<Brain.Contracts.INeuron>(connectionKey);
            await connection.InvokeAsync(new Brain.Contracts.NeuronInvocation(
                "connection.complete-auth.v1",
                System.Text.Json.JsonSerializer.Serialize(new { code, state }),
                Guid.NewGuid().ToString("N"),
                callerKey));
            status = "connected";
        }
        catch (Brain.Contracts.BrainException exception)
        {
            status = exception.Code == Brain.Contracts.BrainErrors.ProviderError
                ? "provider-error"
                : "invalid-state";
        }
        catch
        {
            status = "provider-error";
        }
    }

    return Results.Redirect($"{uiRedirect}?status={Uri.EscapeDataString(status)}");
});
app.Run();
