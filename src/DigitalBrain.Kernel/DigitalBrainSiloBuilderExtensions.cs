using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace DigitalBrain.Kernel;

public static class DigitalBrainSiloBuilderExtensions
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder) => builder.AddDigitalBrain(siloLabel: null);

    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder, string? siloLabel)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(siloLabel))
        {
            metadata[PinToSiloDirector.SiloLabelKey] = siloLabel;
        }

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.UseSiloMetadata(metadata);
        builder.Services.AddPinToSiloPlacement();

        return builder;
    }
}
