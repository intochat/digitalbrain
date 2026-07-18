using System.Reflection;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.Contracts.Messaging;
using TripRadar.Server.Comms.Core.Events;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class ProducerService : IProducerService, IDisposable
{
    private readonly ProducerConfig _producerConfig;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<ProducerService> _logger;

    public ProducerService(IOptions<Kafka> kafkaSettings, ILogger<ProducerService> logger)
    {
        _logger = logger;
        _producerConfig = CreateProducerConfig(kafkaSettings.Value);
        _producer = new ProducerBuilder<string, string>(_producerConfig).Build();
    }

    public async Task ProduceAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : PublishableEvent
    {
        var eventType = @event.GetType();
        var topicAttribute = eventType.GetCustomAttribute<TopicAttribute>();

        if (topicAttribute == null)
        {
            throw new InvalidOperationException(
                $"Event type '{eventType.Name}' does not have a KafkaTopicAttribute. Please add the KafkaTopicAttribute to your event class.");
        }

        await PublishToTopicAsync(topicAttribute.TopicName, @event, cancellationToken);
    }

    private async Task PublishToTopicAsync<T>(string topic, T message, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Attempting to send message to Kafka topic {Topic} using BootstrapServers: {BootstrapServers}",
                topic, _producerConfig.BootstrapServers);

            var messageJson = JsonSerializer.Serialize(message);
            var key = Guid.NewGuid().ToString();

            var result = await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = messageJson },
                cancellationToken);

            _logger.LogInformation(
                "Successfully sent message to Kafka topic {Topic}, Partition: {Partition}, Offset: {Offset}",
                topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to deliver message to Kafka topic {Topic}: {Error}, Reason: {Reason}",
                topic, ex.Message, ex.Error.Reason);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending message to Kafka topic {Topic}: {Error}", topic,
                ex.Message);
            throw;
        }
    }

    public void Dispose() => _producer.Dispose();

    private static ProducerConfig CreateProducerConfig(Kafka settings)
    {
        var securityProtocol = Enum.TryParse<SecurityProtocol>(settings.SecurityProtocol, true, out var protocol)
            ? protocol
            : SecurityProtocol.Plaintext;

        var hasSaslCredentials = !string.IsNullOrWhiteSpace(settings.SaslUsername)
                                 && !string.IsNullOrWhiteSpace(settings.SaslPassword);

        if (securityProtocol is SecurityProtocol.SaslSsl or SecurityProtocol.SaslPlaintext && !hasSaslCredentials)
            securityProtocol = SecurityProtocol.Plaintext;

        var config = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            SecurityProtocol = securityProtocol,
        };

        if (securityProtocol is SecurityProtocol.SaslSsl or SecurityProtocol.SaslPlaintext)
        {
            config.SaslMechanism = Enum.TryParse<SaslMechanism>(settings.SaslMechanism, true, out var mechanism)
                ? mechanism
                : SaslMechanism.Plain;
            config.SaslUsername = settings.SaslUsername;
            config.SaslPassword = settings.SaslPassword;
        }

        return config;
    }
}
