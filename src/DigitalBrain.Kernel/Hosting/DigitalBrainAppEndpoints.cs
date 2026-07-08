using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.TabularData;
using DigitalBrain.Kernel.Uploads;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Kernel.Hosting;

public static class DigitalBrainAppEndpoints
{
    public static WebApplication MapDigitalBrainHandlers(this WebApplication app)
    {
        app.MapPost("/upload", async (HttpRequest request, IGrainFactory grains, ILogger<DigitalBrainAppEndpointLogs> logger) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest("Expected multipart/form-data.");
            }

            var requestAborted = request.HttpContext.RequestAborted;
            var form = await request.ReadFormAsync(cancellationToken: requestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest("No file uploaded.");
            }

            var clientId = form["clientId"].FirstOrDefault();
            var workspaceId = form["workspaceId"].FirstOrDefault();
            var kind = ChatUploadClassifier.Classify(file.FileName);

            if (kind == ChatUploadKind.SqliteDatabase)
            {
                var tempPath = ChatUploadClassifier.TempDatabasePath(file.FileName);
                try
                {
                    await using (var temp = File.Create(tempPath))
                    {
                        await file.CopyToAsync(temp, requestAborted);
                    }

                    var cmd = ChatUploadClassifier.BuildDbInspectSchema(file.FileName, tempPath, clientId, workspaceId);
                    var db = grains.GetGrain<IDbSupportNeuron>(IDbSupportNeuron.SingletonKey);
                    await db.FireAsync(cmd, requestAborted);

                    var dbTimeline = await db.GetTimelineAsync(requestAborted);
                    var inspected = dbTimeline
                        .OfType<DbSchemaInspected>()
                        .LastOrDefault(result => result.CorrelationId == cmd.SynapseId)
                        ?? dbTimeline.OfType<DbSchemaInspected>().LastOrDefault(result => result.ConnectionName == cmd.ConnectionName);

                    if (inspected is not null)
                    {
                        var schemaIno = grains.GetGrain<IInoNeuron>("ino-main");
                        await schemaIno.FireAsync(inspected, requestAborted);
                    }

                    return Results.Ok();
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Could not delete temporary SQLite upload copy {FileName}.", Path.GetFileName(tempPath));
                    }
                }
            }

            if (kind != ChatUploadKind.TabularWorkbook)
            {
                return Results.BadRequest("Unsupported upload type.");
            }

            using var fileStream = new MemoryStream();
            await file.CopyToAsync(fileStream, requestAborted);
            var dataset = TabularDataParser.Parse(fileStream.ToArray());

            var ino = grains.GetGrain<IInoNeuron>("ino-main");
            await ino.FireAsync(new TabularDataIngested(
                file.FileName,
                System.Text.Json.JsonSerializer.Serialize(dataset.Headers),
                System.Text.Json.JsonSerializer.Serialize(dataset.Rows),
                System.Text.Json.JsonSerializer.Serialize(dataset.ColumnStats),
                clientId,
                workspaceId), requestAborted);

            return Results.Ok();
        });

        app.MapGet("/oauth/callback/{provider}", async (
            string provider,
            HttpRequest request,
            IServiceProvider sp,
            ILogger<DigitalBrainAppEndpointLogs> logger) =>
        {
            var connector = sp.GetRequiredKeyedService<DigitalBrain.Kernel.Abstractions.IConnector>(provider);
            var cb = new DigitalBrain.Kernel.Abstractions.OAuthCallback(
                Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                Error: request.Query["error"].FirstOrDefault(),
                ErrorDescription: request.Query["error_description"].FirstOrDefault(),
                FallbackRedirectUri: request.Query["redirect_uri"].FirstOrDefault());
            var requestAborted = request.HttpContext.RequestAborted;
            var result = await connector.CompleteAuthAsync(cb, requestAborted);
            if (result.Success)
            {
                try
                {
                    var gf = sp.GetService<IGrainFactory>();
                    if (gf is not null)
                    {
                        var uid = cb.State?.Split(':')[0] ?? "default";
                        var uscope = PackConfigScopes.ForUser(new UserId(uid));
                        var ikey = "google-auth-completed";
                        var ing = gf.GetGrain<IIngressNeuron>(ikey);
                        var p = new Dictionary<string, object?>
                        {
                            ["provider"] = provider,
                            ["pack"] = provider == "google" ? GoogleClientFactory.PackName : "salesforce",
                            ["userId"] = uid,
                            ["scope"] = uscope
                        };
                        await ing.IngestAsync("PackConfigured", p, requestAborted);
                        var sigName = provider == "google" ? GoogleSignals.AuthCompleted : SalesforceSignals.AuthCompleted;
                        await ing.IngestAsync(sigName, p, requestAborted);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "OAuth completion signal could not be published for {Provider}.", provider);
                }
            }
            var title = result.Success ? "Success" : "Error";
            var msg = result.Success ? "Authentication completed." : (result.Error + ": " + result.Details);
            return Results.Content($"<html><body><h1>{title}</h1><p>{msg}</p><p>Provider: {provider}</p></body></html>", "text/html",
                statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });

        return app;
    }

    private sealed class DigitalBrainAppEndpointLogs;
}
