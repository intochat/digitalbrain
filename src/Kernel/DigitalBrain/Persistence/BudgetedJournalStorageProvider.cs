using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class BudgetedJournalStorageProvider(IJournalStorageProvider inner, TimeSpan budget) : IJournalStorageProvider
{
    public IJournalStorage CreateStorage(JournalId journalId) => new BudgetedJournalStorage(inner.CreateStorage(journalId), budget);
}
