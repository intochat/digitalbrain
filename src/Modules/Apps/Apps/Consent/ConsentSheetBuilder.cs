using DigitalBrain.Apps;

namespace DigitalBrain.Apps.Consent;

// Builds the consent sheet from the manifest. The Compute figure is a shadow estimate: the review
// shows what a typical run could cost, and nothing is charged at install or review time.
public static class ConsentSheetBuilder
{
    private const int SampleCalls = 10;
    private const decimal BasePlanningCompute = 5m;

    public static AppConsentSheet Build(AppManifest manifest, bool approved)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new AppConsentSheet
        {
            AppId = manifest.Id,
            Version = manifest.Version,
            Name = manifest.Name,
            DescriptionForPeople = manifest.DescriptionForPeople,
            Examples = manifest.ExamplePrompts,
            DataTypes = DataTypes(manifest),
            Meters = manifest.Meters,
            EstimatedCompute = ShadowEstimate(manifest),
            Approved = approved,
        };
    }

    public static decimal ShadowEstimate(AppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var perCall = manifest.Meters.Sum(meter => meter.ProposedPriceInCompute ?? 0m);
        var estimate = perCall * SampleCalls;
        return estimate > 0m ? estimate : BasePlanningCompute;
    }

    private static IReadOnlyList<ConsentDataType> DataTypes(AppManifest manifest)
    {
        var types = new List<ConsentDataType>();
        foreach (var permission in manifest.Permissions)
        {
            types.Add(new ConsentDataType
            {
                SemanticTypeId = permission.SemanticTypeId,
                Reason = permission.Reason,
                Write = permission.Write,
            });
        }

        foreach (var operation in manifest.Operations)
        {
            foreach (var typeId in operation.InputTypeIds.Values
                .Append(operation.OutputTypeId)
                .Where(typeId => !string.IsNullOrWhiteSpace(typeId) && typeId != "reference"))
            {
                if (types.Any(existing => existing.SemanticTypeId == typeId)) { continue; }
                types.Add(new ConsentDataType
                {
                    SemanticTypeId = typeId!,
                    Reason = $"Used by operation '{operation.Name}'.",
                });
            }
        }

        return types;
    }
}
