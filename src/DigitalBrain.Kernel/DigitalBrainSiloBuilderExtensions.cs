using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace DigitalBrain;

public static class DigitalBrainSiloBuilderExtensions
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddJournalStorage();
        builder.UseJsonJournalFormat(JournalJsonContext.Default);
        builder.AddIncomingGrainCallFilter<OwnerBoundCallFilter>();
        builder.UseSiloMetadata(new Dictionary<string, string>(StringComparer.Ordinal));
        builder.Services.AddPinToSiloPlacement();

        return builder;
    }
}
