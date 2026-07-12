using System.Security.Cryptography;
using Azure.Data.Tables;
using Azure.Identity;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.RuntimeMigration;

public static class Program
{
    public static Task<int> Main(string[] args) => RuntimeMigrationCommand.RunAsync(args, Console.Out);
}

public static class RuntimeMigrationCommand
{
    private const string OperationDomain = "digitalbrain.v2.operations";
    private const string ConversationDomain = "digitalbrain.v2.ino-effects";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        var output = new MigrationOutput(writer);
        var stage = "initialization";
        try
        {
            var mode = MigrationModeParser.Parse(args);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
            builder.Logging.ClearProviders();
            stage = "source-location";
            var paths = LegacyDataRootLocator.Locate(builder.Configuration);
            var legacyKey = ReadLegacyKey(builder.Configuration["DigitalBrain:RuntimeMigration:LegacyJournalHmacKey"]);
            try
            {
                stage = "source-read";
                using var sourceLease = paths.AcquireExclusive();
                using var operationReader = new ReadOnlyAuthenticatedJournalReader(
                    OperationDomain,
                    legacyKey,
                    paths.Operations);
                using var conversationReader = new ReadOnlyAuthenticatedJournalReader(
                    ConversationDomain,
                    legacyKey,
                    paths.Conversations);
                var operationJournal = operationReader.Read();
                var conversationJournal = conversationReader.Read();
                stage = "planning";
                var planner = new LegacyMigrationPlanner(ReadExpectedOAuthOrigin(
                    builder.Configuration["DigitalBrain:RuntimeMigration:ExpectedOAuthOrigin"]));
                var plan = planner.Plan(operationJournal, conversationJournal);
                if (mode == MigrationMode.DryRun)
                {
                    output.Write(plan, mode, "verified");
                    return 0;
                }
                if (!string.Equals(
                        builder.Configuration["DigitalBrain:RuntimeMigration:RuntimeQuiesced"],
                        "true",
                        StringComparison.OrdinalIgnoreCase))
                    throw new MigrationGapException("runtime-not-quiesced");

                stage = "client-configuration";
                ConfigureOrleansClient(builder);
                stage = "host-build";
                using var host = builder.Build();
                using (var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    stage = "host-start";
                    startTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try { await host.StartAsync(startTimeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new MigrationGapException("destination-unavailable");
                    }
                    catch
                    {
                        throw new MigrationGapException("destination-unavailable");
                    }
                }
                try
                {
                    stage = "destination-apply";
                    var cluster = host.Services.GetRequiredService<IClusterClient>();
                    string destinationDigest;
                    try
                    {
                        destinationDigest = await new ConversationMigrationApplier(cluster)
                            .ApplyAndVerifyAsync(plan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (MigrationGapException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw new MigrationGapException("destination-apply-failed");
                    }
                    if (!string.Equals(destinationDigest, plan.ExpectedDigest, StringComparison.Ordinal))
                        throw new MigrationGapException("destination-aggregate-mismatch");
                    stage = "source-recheck";
                    var operationRecheck = operationReader.Read();
                    var conversationRecheck = conversationReader.Read();
                    if (operationRecheck.Sequence != operationJournal.Sequence ||
                        !string.Equals(operationRecheck.HeadDigest, operationJournal.HeadDigest, StringComparison.Ordinal) ||
                        conversationRecheck.Sequence != conversationJournal.Sequence ||
                        !string.Equals(conversationRecheck.HeadDigest, conversationJournal.HeadDigest, StringComparison.Ordinal))
                        throw new MigrationGapException("legacy-source-changed");
                    var marker = new MigrationMarker(
                        plan.SchemaVersion,
                        plan.SourceDigest,
                        plan.MigrationId,
                        destinationDigest,
                        plan.Conversations.Count,
                        plan.TurnCount,
                        plan.ActiveOperationCount,
                        plan.TerminalOperationCount);
                    stage = "marker-write";
                    try
                    {
                        using var markerStore = MigrationMarkerStore.Create(builder.Configuration);
                        await markerStore.EnsureAsync(marker, cancellationToken).ConfigureAwait(false);
                    }
                    catch (MigrationGapException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw new MigrationGapException("marker-write-failed");
                    }
                    stage = "source-freeze";
                    try
                    {
                        paths.FreezeAll();
                    }
                    catch
                    {
                        throw new MigrationGapException("legacy-freeze-failed");
                    }
                }
                finally
                {
                    using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await host.StopAsync(stopTimeout.Token).ConfigureAwait(false); }
                    catch { }
                }
                stage = "completion-output";
                output.Write(plan, mode, "complete");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(legacyKey);
            }
        }
        catch (OperationCanceledException)
        {
            output.WriteFailure("migration-cancelled");
            return 2;
        }
        catch (MigrationGapException exception)
        {
            output.WriteFailure(SafeCode(exception.Code));
            return 2;
        }
        catch (Exception exception)
        {
            output.WriteFailure("migration-" + stage + "-" + ExceptionCategory(exception));
            return 2;
        }
    }

    private static void ConfigureOrleansClient(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Orleans:Clustering:ProviderType"] ??
                       Environment.GetEnvironmentVariable("Orleans__Clustering__ProviderType");
        if (!string.Equals(provider, "AzureTableStorage", StringComparison.OrdinalIgnoreCase))
            throw new MigrationGapException("orleans-clustering-unavailable");

        var serviceKey = builder.Configuration["Orleans:Clustering:ServiceKey"] ??
                         Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ??
                         "clustering";
        var connection = builder.Configuration.GetConnectionString(serviceKey);
        TableServiceClient tables;
        if (!string.IsNullOrWhiteSpace(connection))
        {
            tables = new TableServiceClient(connection);
        }
        else
        {
            var accountName = builder.Configuration["DigitalBrain:Storage:AccountName"];
            if (string.IsNullOrWhiteSpace(accountName) ||
                !accountName.All(static character => char.IsAsciiLetterOrDigit(character)))
                throw new MigrationGapException("orleans-clustering-unavailable");
            tables = new TableServiceClient(
                new Uri($"https://{accountName}.table.core.windows.net"),
                new DefaultAzureCredential());
        }

        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "digitalbrain";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "digitalbrain";
        try
        {
            builder.UseOrleansClient(client =>
            {
                client.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = clusterId;
                    options.ServiceId = serviceId;
                });
                client.UseAzureStorageClustering(options => options.TableServiceClient = tables);
            });
        }
        catch (InvalidOperationException)
        {
            throw new MigrationGapException("orleans-client-registration-failed");
        }
    }

    private static byte[] ReadLegacyKey(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 8192)
            throw new MigrationGapException("legacy-key-missing");
        byte[] key;
        try { key = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new MigrationGapException("legacy-key-invalid"); }
        if (key.Length >= 32) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new MigrationGapException("legacy-key-invalid");
    }

    private static Uri? ReadExpectedOAuthOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value, UriKind.Absolute, out var origin)
            ? origin
            : throw new MigrationGapException("oauth-origin-invalid");
    }

    private static string SafeCode(string value)
    {
        if (value.Length is > 0 and <= 64 && value.All(static character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return value;
        return "migration-failed";
    }

    private static string ExceptionCategory(Exception exception) => exception switch
    {
        ArgumentException => "invalid-configuration",
        InvalidOperationException => "invalid-operation",
        IOException => "io-failed",
        _ => "failed"
    };
}
