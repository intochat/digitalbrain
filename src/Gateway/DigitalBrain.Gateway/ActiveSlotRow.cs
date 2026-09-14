using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Gateway;

// The kernel's lease row is the truth about which slot is live; the gateway reads it once at startup so a
// restart of the gateway alone does not send traffic to the slot that was replaced.
internal static class ActiveSlotRow
{
    internal static async Task<string?> ReadOwnerAsync(TableServiceClient tables, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            var response = await tables.GetTableClient(ActiveSlotNames.Table)
                .GetEntityAsync<TableEntity>(ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value.GetString(ActiveSlotNames.Owner);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // Nothing has been promoted yet; configuration decides.
            return null;
        }
        catch (RequestFailedException error)
        {
            logger.LogWarning(error, "The active-slot lease row could not be read; the configured slot stays active.");
            return null;
        }
    }
}
