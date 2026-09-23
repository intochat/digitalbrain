namespace DigitalBrain.Marketplace.Creators;

// D7 (prepaid Compute, self-serve sign-up) and D16 (consumer terms) are not ratified. Each flag is
// read from configuration and defaults to off; turning one on is an owner decision, not code.
public sealed class CreatorPublishingOptions
{
    public const string SectionKey = "Marketplace:Creators";

    public bool SelfServeSignUp { get; init; }

    public bool PrepaidCompute { get; init; }

    public bool ConsumerTerms { get; init; }
}
