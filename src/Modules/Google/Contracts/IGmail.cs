using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Google;

[Alias("gmail")]
public interface IGmail : INeuron
{
    [Alias("connect")]
    [NeuronTool]
    Task<Accepted<SignalId>> Connect(ConnectGmailAccount command);

    /// <summary>Requires a stored refresh token; wait for GmailRefreshed before retrying a read.</summary>
    [Alias("refresh")]
    [NeuronTool]
    Task<Accepted<GmailConnection>> Refresh(RefreshGmailConnection command);

    [Alias("disconnect")]
    [NeuronTool]
    Task<Accepted<GmailConnection>> Disconnect(DisconnectGmail command);

    [Alias("prepare-draft")]
    [NeuronTool]
    Task<Accepted<GmailDraftPreview>> PrepareDraft(PrepareGmailDraft command);

    /// <summary>Consumes the preview once; its id and schema hash must match the prepared preview.</summary>
    [Alias("confirm-draft")]
    [NeuronTool]
    Task<Accepted<GmailDraftPreview>> ConfirmDraft(ConfirmGmailDraft command);

    [ReadOnly, Alias("connection")]
    [NeuronTool(IsReadOnly = true)]
    Task<GmailConnection> ReadConnection();

    [ReadOnly, Alias("search")]
    [NeuronTool(IsReadOnly = true)]
    Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("thread")]
    [NeuronTool(IsReadOnly = true)]
    Task<GmailContentRead> ReadThread(ReadGmailThread query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("labels")]
    [NeuronTool(IsReadOnly = true)]
    Task<GmailContentRead> ReadLabels(CancellationToken cancellationToken = default);
}
