using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications;
using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Tests.Notifications;

public class ScheduledQueryConsumerTests
{
    private const string Topic = "flight-queries";

    private static ConsumeResult<string, string> MakeResult() => new()
    {
        Topic = Topic,
        Partition = new Partition(0),
        Offset = new Offset(42),
        Message = new Message<string, string> { Key = "key", Value = "{\"foo\":1}" }
    };

    private static Mock<IScheduledQueryHandler> HandlerFor(string topic)
    {
        var mock = new Mock<IScheduledQueryHandler>();
        mock.SetupGet(h => h.Topic).Returns(topic);
        mock.Setup(h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ScheduledQueryConsumer Create(
        Mock<IConsumer<string, string>> consumer,
        IScheduledQueryHandler handler)
    {
        return new ScheduledQueryConsumer(
            consumer.Object,
            [handler],
            Options.Create(new KafkaConsumerOptions()),
            NullLogger<ScheduledQueryConsumer>.Instance)
        {
            Backoff = _ => TimeSpan.Zero
        };
    }

    [Fact]
    public async Task ProcessMessage_HandlerSucceeds_CommitsOffset()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var handler = HandlerFor(Topic);
        var sut = Create(consumer, handler.Object);
        var result = MakeResult();

        await sut.ProcessMessageAsync(result, CancellationToken.None);

        handler.Verify(h => h.HandleAsync("{\"foo\":1}", It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_NoHandlerRegisteredForTopic_CommitsOffsetAndSkips()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var handler = HandlerFor("different-topic");
        var sut = Create(consumer, handler.Object);
        var result = MakeResult();

        await sut.ProcessMessageAsync(result, CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        consumer.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_HandlerFailsOnceThenSucceeds_RetriesAndCommitsOnce()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var attempts = 0;
        var handler = new Mock<IScheduledQueryHandler>();
        handler.SetupGet(h => h.Topic).Returns(Topic);
        handler.Setup(h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new InvalidOperationException("transient"))
                    : Task.CompletedTask;
            });
        var sut = Create(consumer, handler.Object);
        var result = MakeResult();

        await sut.ProcessMessageAsync(result, CancellationToken.None);

        attempts.Should().Be(2);
        consumer.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_HandlerFailsAllAttempts_CommitsAsPoisonPill()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        var handler = new Mock<IScheduledQueryHandler>();
        handler.SetupGet(h => h.Topic).Returns(Topic);
        handler.Setup(h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("poison"));
        var sut = Create(consumer, handler.Object);
        var result = MakeResult();

        await sut.ProcessMessageAsync(result, CancellationToken.None);

        handler.Verify(
            h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(ScheduledQueryConsumer.MaxAttempts));
        consumer.Verify(c => c.Commit(result), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_CancellationDuringHandler_DoesNotCommit()
    {
        var consumer = new Mock<IConsumer<string, string>>();
        using var cts = new CancellationTokenSource();
        var handler = new Mock<IScheduledQueryHandler>();
        handler.SetupGet(h => h.Topic).Returns(Topic);
        handler.Setup(h => h.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var sut = Create(consumer, handler.Object);
        var result = MakeResult();

        var act = () => sut.ProcessMessageAsync(result, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }
}
