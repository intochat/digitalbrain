using Orleans.Concurrency;

namespace DigitalBrain.Contracts.Integrations;

// Request an account of a kind. The secret stays in the platform ring; the script holds an AccountRef.
[Alias("integrations.accounts"), Orleans.Metadata.DefaultGrainType("integrations.accounts")]
public interface IConnectionRequests : INeuron
{
    [ReadOnly] Task<IntegrationKind[]> ListKinds();

    [ReadOnly] Task<AccountRef[]> ListPending();

    Task<AccountRef> Request(string kind);
}

[GenerateSerializer, Alias("integrations.kind")]
public sealed record IntegrationKind([property: Id(0)] string Id, [property: Id(1)] string DisplayName);

[GenerateSerializer, Alias("integrations.account-requested")]
public sealed record AccountRequested([property: Id(0)] string Kind, [property: Id(1)] string Id) : Signal;
