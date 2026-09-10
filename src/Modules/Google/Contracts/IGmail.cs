using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Google;

[Alias("gmail")]
public interface IGmail : INeuron
{
    [Alias("connect")]
    Task<Accepted<GmailConnection>> Connect(ConnectGmailAccount command);

    /// <summary>Requires a stored refresh token; wait for GmailRefreshed before retrying a read.</summary>
    [Alias("refresh")]
    Task<Accepted<GmailConnection>> Refresh(RefreshGmailConnection command);

    [Alias("disconnect")]
    Task<Accepted<GmailConnection>> Disconnect(DisconnectGmail command);

    [Alias("prepare-draft")]
    Task<Accepted<GmailDraftPreview>> PrepareDraft(PrepareGmailDraft command);

    /// <summary>Consumes the preview once; its id and schema hash must match the prepared preview.</summary>
    [Alias("confirm-draft")]
    Task<Accepted<GmailDraftPreview>> ConfirmDraft(ConfirmGmailDraft command);

    [ReadOnly, Alias("connection")]
    Task<GmailConnection> ReadConnection();

    [ReadOnly, Alias("search")]
    Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("thread")]
    Task<GmailContentRead> ReadThread(ReadGmailThread query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("labels")]
    Task<GmailContentRead> ReadLabels(CancellationToken cancellationToken = default);
}
