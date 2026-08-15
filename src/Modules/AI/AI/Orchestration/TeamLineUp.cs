namespace DigitalBrain.AI;

public sealed class TeamLineUp
{
    private const string NamePrefix = "team";

    private TeamLineUp(string teamName, TeamFormation formation)
    {
        TeamName = teamName;
        Formation = formation;
    }

    public string TeamName { get; }

    public TeamFormation Formation { get; }

    public static IReadOnlyList<string> KnownModels => ModelContracts.KnownModelNames();

    public static TeamLineUp Of(IEnumerable<string> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        string[] lineUp = [.. Normalized([.. models]).Order(StringComparer.Ordinal)];

        return new TeamLineUp(string.Join('-', [NamePrefix, .. lineUp]), new TeamFormation(lineUp));
    }

    internal static IReadOnlyList<string> Normalized(IReadOnlyList<string>? models)
    {
        if (models is not { Count: > 0 })
        {
            throw new ArgumentException("A team formation must name at least one model.", nameof(models));
        }

        List<string> lineUp = [];

        foreach (var requested in models)
        {
            var model = ModelContracts.ModelNameOf(ModelContracts.Resolve(requested));

            if (lineUp.Contains(model, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Model '{model}' is named more than once; a team runs each of its models exactly once.",
                    nameof(models));
            }

            lineUp.Add(model);
        }

        return lineUp;
    }
}
