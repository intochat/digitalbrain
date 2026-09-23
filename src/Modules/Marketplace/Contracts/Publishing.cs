namespace DigitalBrain.Marketplace;

// Publish rings gate who may list: first-party apps and invited remote developers only (D11).
public enum PublishRing
{
    FirstParty = 0,
    InvitedRemote = 1,
}

public enum ListingStatus
{
    Certified = 0,
    Beta = 1,
    Published = 2,
    TakenDown = 3,
    Disabled = 4,
}

// A publisher's proven namespace. The domain is the one a DNS-TXT challenge must answer for.
[GenerateSerializer, Alias("marketplace.publisher")]
public sealed record PublisherProfile
{
    [Id(0)] public required string PublisherId { get; init; }
    [Id(1)] public required string Namespace { get; init; }
    [Id(2)] public required string Domain { get; init; }
    [Id(3)] public required PublishRing Ring { get; init; }
    [Id(4)] public required bool Invited { get; init; }
    [Id(5)] public required DateTimeOffset RegisteredAt { get; init; }
}
