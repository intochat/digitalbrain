using System.Buffers;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BudgetedJournalStorageFacts
{
    [Fact]
    public void RegistrationWrapsStorageFromTheRegisteredSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new NeuronOptions());
        services.AddSingleton<RecordingJournalStorageProvider>();
        services.AddSingleton<IJournalStorageProvider>(provider => provider.GetRequiredService<RecordingJournalStorageProvider>());

        services.UseBudgetedJournalStorage();

        using var provider = services.BuildServiceProvider();
        var singleton = provider.GetRequiredService<RecordingJournalStorageProvider>();
        var budgeted = Assert.IsType<BudgetedJournalStorageProvider>(provider.GetRequiredService<IJournalStorageProvider>());
        var journalId = new JournalId("counter/c");
        Assert.IsType<BudgetedJournalStorage>(budgeted.CreateStorage(journalId));

        Assert.Equal(journalId, Assert.Single(singleton.RequestedJournals));
        Assert.Same(budgeted, provider.GetRequiredService<IJournalStorageProvider>());
    }

    [Fact]
    public void CompactionRequestReflectsInnerStorageFlag()
    {
        var inner = new SettlingJournalStorage();
        var storage = new BudgetedJournalStorage(inner, TimeSpan.FromSeconds(1));

        Assert.False(storage.IsCompactionRequested);
        inner.IsCompactionRequested = true;
        Assert.True(storage.IsCompactionRequested);
    }

    [Fact]
    public async Task ReadWaitsForInnerStorageToSettleAfterBudgetCancellation()
    {
        var inner = new SettlingJournalStorage();
        var storage = new BudgetedJournalStorage(inner, TimeSpan.FromMilliseconds(30));

        // The budget decorator must await inner storage settlement instead of abandoning an active storage call.
        await Assert.ThrowsAsync<OperationCanceledException>(() => storage.ReadAsync(null!, CancellationToken.None).AsTask());

        Assert.True(inner.Settled);
    }

    private sealed class RecordingJournalStorageProvider : IJournalStorageProvider
    {
        public List<JournalId> RequestedJournals { get; } = [];
        public SettlingJournalStorage Storage { get; } = new();

        public IJournalStorage CreateStorage(JournalId journalId)
        {
            RequestedJournals.Add(journalId);
            return Storage;
        }
    }

    private sealed class SettlingJournalStorage : IJournalStorage
    {
        public bool IsCompactionRequested { get; set; }
        public bool Settled { get; private set; }

        public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Finish asynchronous cleanup after cancellation to expose decorators that abandon the operation.
                await Task.Delay(30, CancellationToken.None);
                Settled = true;
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
