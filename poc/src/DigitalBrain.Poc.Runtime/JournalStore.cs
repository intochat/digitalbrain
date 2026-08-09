namespace DigitalBrain.Poc.Runtime;

using System.Text.Json;
using DigitalBrain.Poc.Abstractions;

public sealed class JournalStore
{
    private readonly RunStore _store;

    public JournalStore(PocDataRoot root)
    {
        _store = new RunStore(root);
    }

    public Task<IReadOnlyList<string>> ReadKindsAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<string>>(
            document => document.Journal.Select(entry => entry.Kind).ToArray(),
            cancellationToken);

    public Task<bool> HasAcknowledgedReceiptAsync(
        string receiptId,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(
            document => document.AcknowledgedReceipts.Contains(receiptId),
            cancellationToken);

    public Task<IReadOnlyList<TFact>> FindAsync<TFact>(
        CancellationToken cancellationToken = default)
        where TFact : Synapse =>
        _store.ReadAsync<IReadOnlyList<TFact>>(
            document => document.Journal
                .Where(entry =>
                    entry.Direction == "fact" &&
                    entry.Kind == typeof(TFact).Name &&
                    entry.PayloadJson is not null)
                .Select(entry => JsonSerializer.Deserialize<TFact>(entry.PayloadJson!) ??
                    throw new InvalidDataException(
                        $"Terminal journal fact '{entry.Kind}' deserialized to null."))
                .ToArray(),
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadReceiptIdsAsync(
        string kind,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<string>>(
            document => document.Journal
                .Where(entry => entry.Kind == kind)
                .Select(entry => entry.ReceiptId)
                .ToArray(),
            cancellationToken);
}
