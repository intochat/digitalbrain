using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Google;

[Alias("gmail")]
public interface IGmail : INeuron
{
    /// <summary>Connects the selected Google account.</summary>
    [Alias("connect")]
    Task<Accepted<GmailConnection>> Connect(ConnectGmailAccount command);

    /// <summary>Disconnects the selected Google account.</summary>
    [Alias("disconnect")]
    Task<Accepted<GmailConnection>> Disconnect(DisconnectGmail command);

    /// <summary>Prepares a draft preview without creating a draft.</summary>
    [Alias("prepare-draft")]
    Task<Accepted<GmailDraftPreview>> PrepareDraft(PrepareGmailDraft command);

    /// <summary>Creates a draft from the confirmed preview.</summary>
    [Alias("confirm-draft")]
    Task<Accepted<GmailDraftPreview>> ConfirmDraft(ConfirmGmailDraft command);

    /// <summary>Reads the stored Gmail connection.</summary>
    [ReadOnly, Alias("connection")]
    Task<GmailConnection> ReadConnection();

    /// <summary>Searches Gmail threads with bounded minimal content.</summary>
    [ReadOnly, Alias("search")]
    Task<GmailContentRead> SearchThreads(SearchGmailThreads query, CancellationToken cancellationToken = default);

    /// <summary>Reads a Gmail thread in a safe content format.</summary>
    [ReadOnly, Alias("thread")]
    Task<GmailContentRead> ReadThread(ReadGmailThread query, CancellationToken cancellationToken = default);

    /// <summary>Reads Gmail labels.</summary>
    [ReadOnly, Alias("labels")]
    Task<GmailContentRead> ReadLabels(CancellationToken cancellationToken = default);
}
