using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record GmailDigestReady([property: Id(1)] string UserAccountId,
    [property: Id(2)] string DatabaseId,
    [property: Id(3)] int RowsWritten
) : Synapse;
