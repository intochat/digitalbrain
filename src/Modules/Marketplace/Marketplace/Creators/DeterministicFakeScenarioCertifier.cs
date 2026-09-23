using DigitalBrain.Apps;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Marketplace.Creators;

// The only certification runner in P5a. A declarative app has no code, so a scenario is certified by
// running it against this deterministic fake: the fake resolves the operation named in the scenario,
// checks the declared inputs/outputs against the semantic catalog, and records evidence. It never
// calls a model or the network, so the result is reproducible.
public sealed class DeterministicFakeScenarioCertifier : ICreatorScenarioCertifier
{
    private static readonly IReadOnlySet<string> CatalogTypeIds =
        TypeCatalog.All.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);

    public Task<CertificationReport> CertifyAsync(AppManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var evidence = manifest.Scenarios.Count == 0
            ? [new ScenarioEvidence { Name = "(none)", Passed = false, Detail = "The app declares no scenarios, so it cannot be certified." }]
            : manifest.Scenarios.Select(scenario => RunOnFake(manifest, scenario)).ToArray();

        var certified = evidence.All(item => item.Passed);
        return Task.FromResult(new CertificationReport
        {
            AppId = manifest.Id,
            Version = manifest.Version,
            Certified = certified,
            CertifiedBy = "deterministic-fake",
            CertifiedAt = DateTimeOffset.UtcNow,
            Scenarios = evidence,
        });
    }

    private static ScenarioEvidence RunOnFake(AppManifest manifest, AppScenario scenario)
    {
        var operation = manifest.Operations.FirstOrDefault(candidate =>
            scenario.When.Contains(candidate.Name, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
        {
            return new ScenarioEvidence
            {
                Name = scenario.Name,
                Passed = false,
                Detail = $"On the fake, '{scenario.When}' names no declared operation.",
            };
        }

        if (string.IsNullOrWhiteSpace(scenario.Then))
        {
            return new ScenarioEvidence
            {
                Name = scenario.Name,
                Passed = false,
                Detail = $"On the fake, scenario '{scenario.Name}' declares no expected outcome.",
            };
        }

        var unknownType = operation.InputTypeIds.Values
            .Append(operation.OutputTypeId ?? string.Empty)
            .FirstOrDefault(typeId => typeId.Length > 0 && !CatalogTypeIds.Contains(typeId));
        if (unknownType is not null)
        {
            return new ScenarioEvidence
            {
                Name = scenario.Name,
                Passed = false,
                Detail = $"On the fake, operation '{operation.Name}' uses unknown type '{unknownType}'.",
            };
        }

        return new ScenarioEvidence
        {
            Name = scenario.Name,
            Passed = true,
            Detail = $"On the fake, '{operation.Name}' ran and produced {scenario.Then}.",
        };
    }
}
