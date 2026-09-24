namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-proposal")]
public sealed record PackageProposal(
    [property: Id(0)] int Number,
    [property: Id(1)] PackageRevisionRef Source,
    [property: Id(2)] string Title,
    [property: Id(3)] string Author,
    [property: Id(4)] ProposalStatus Status,
    [property: Id(5)] DateTimeOffset OpenedAt);
