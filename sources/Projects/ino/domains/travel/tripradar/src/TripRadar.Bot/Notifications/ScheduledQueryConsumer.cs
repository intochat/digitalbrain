using System.Diagnostics.Metrics;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Notifications;

internal sealed class ScheduledQueryConsumer(
    IConsumer<string, string> consumer,
    IEnumerable<IScheduledQueryHandler> handlers,
    IOptions<KafkaConsumerOptions> options,
    ILogger<ScheduledQueryConsumer> logger) : BackgroundService
{
    public const string MeterName = "TripRadar.Bot.Notifications";
    internal const int MaxAttempts = 3;

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PoisonPillCounter =
        Meter.CreateCounter<long>("scheduled_query_poison_pill_total");

    private readonly Dictionary<string, IScheduledQueryHandler> _handlersByTopic =
        handlers.ToDictionary(h => h.Topic, StringComparer.OrdinalIgnoreCase);

    internal Func<int, TimeSpan> Backoff { get; init; } =
        attempt => TimeSpan.FromSeconds(1 << (attempt - 1));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var topics = options.Value.Topics;
        if (topics is null || topics.Length == 0)
        {
            logger.LogWarning("No Kafka topics configured for ScheduledQueryConsumer; nothing to subscribe to.");
            return;
        }

        consumer.Subscribe(topics);
        logger.LogInformation("Subscribed to Kafka topics: {Topics}", string.Join(", ", topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                {
                    logger.LogWarning("One or more topics not available yet, retrying in 10s. {Reason}", ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                    continue;

                await ProcessMessageAsync(result, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Scheduled query consumer stopped.");
        }
        finally
        {
            consumer.Close();
        }
    }

    internal async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        if (!_handlersByTopic.TryGetValue(result.Topic, out var handler))
        {
            logger.LogWarning(
                "No handler registered for topic {Topic}; skipping and committing offset {Offset}",
                result.Topic, result.Offset);
            consumer.Commit(result);
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await handler.HandleAsync(result.Message.Value, ct);
                consumer.Commit(result);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= MaxAttempts)
                {
                    logger.LogError(ex,
                        "Handler for topic {Topic} offset {Offset} failed after {MaxAttempts} attempts; skipping poison pill",
                        result.Topic, result.Offset, MaxAttempts);
                    PoisonPillCounter.Add(1, new KeyValuePair<string, object?>("topic", result.Topic));
                    consumer.Commit(result);
                    return;
                }

                var delay = Backoff(attempt);
                logger.LogWarning(ex,
                    "Handler for topic {Topic} offset {Offset} failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}",
                    result.Topic, result.Offset, attempt, MaxAttempts, delay);
                await Task.Delay(delay, ct);
            }
        }
    }
}
