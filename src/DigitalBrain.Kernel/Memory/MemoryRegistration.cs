using Azure.Data.Tables;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Memory;

internal static class MemoryRegistration
{
    public static IServiceCollection AddDigitalBrainMemory(this IServiceCollection services, IConfiguration configuration, TableClient? tableClient = null, bool allowInMemory = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        if (tableClient is null)
        {
            var connection = configuration.GetConnectionString(AzureTableMemoryFactStore.FactsTableName);
            if (!string.IsNullOrWhiteSpace(connection))
                tableClient = new TableClient(connection, AzureTableMemoryFactStore.FactsTableName);
        }
        if (tableClient is null)
        {
            if (!allowInMemory && !configuration.GetValue<bool>("DigitalBrain:TestMode"))
                throw new InvalidOperationException("ConnectionStrings:memoryfacts or an explicit memoryfacts TableClient is required.");
            services.AddSingleton<IMemoryFactStore, InMemoryMemoryFactStore>();
        }
        else
        {
            services.AddSingleton(tableClient);
            services.AddSingleton<IMemoryFactStore, AzureTableMemoryFactStore>();
            services.AddSingleton<IHostedService, MemoryTableInitializer>();
        }
        services.AddSingleton<IMemoryAuditSink, LoggingMemoryAuditSink>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<ICapabilityHandler, MemoryRecallCapabilityHandler>();
        services.AddSingleton<ICapabilityHandler, MemoryRememberCapabilityHandler>();
        return services;
    }
}

internal sealed class MemoryTableInitializer(TableClient table) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await table.CreateIfNotExistsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class LoggingMemoryAuditSink(ILogger<LoggingMemoryAuditSink> logger) : IMemoryAuditSink
{
    public ValueTask WriteAsync(MemoryAuditRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Memory operation {Operation} for owner {OwnerId}, actor {ActorId}, fact {FactId}, correlation {CorrelationId} completed with {Outcome}",
            record.Operation,
            record.OwnerId.Value,
            record.ActorId.Value,
            record.FactId,
            record.CorrelationId,
            record.Outcome);
        return ValueTask.CompletedTask;
    }
}
