namespace DigitalBrain.Core.Distribution;

[GenerateSerializer]
public enum BundleTier { Substrate, Channel, Content }

// Telegram (ordinal 1) was retired with the Telegram integration. Web keeps its original ordinal (2) --
// [GenerateSerializer] enums serialize by underlying int value, so reusing 1 for a future member would
// silently reinterpret any already-persisted Telegram value as that new member.
[GenerateSerializer]
public enum BundleChannel { InApp = 0, Web = 2 }

[GenerateSerializer]
[Alias("DigitalBrain.Core.Distribution.ExperienceRef")]
public record ExperienceRef(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] string EntryEvent = "start");

[GenerateSerializer]
[Alias("DigitalBrain.Core.Distribution.BundleDependency")]
public record BundleDependency(
    [property: Id(0)] string PackName,
    [property: Id(1)] string MinVersion);

// Product-level metadata a bundle declares in code (single source of truth). Catalog
// materialization facets by tier/channel without forcing the primitive Core assembly to know packs.
// PackManifest stays separate: it carries dispatch (HandledSynapseTypes) and config requirements.
[GenerateSerializer]
[Alias("DigitalBrain.Core.Distribution.BundleManifest")]
public record BundleManifest(
    [property: Id(0)] BundleTier Tier,
    [property: Id(1)] ExperienceRef? EntryExperience,
    [property: Id(2)] IReadOnlyList<BundleChannel> Channels,
    [property: Id(3)] IReadOnlyList<BundleDependency>? Dependencies = null);
