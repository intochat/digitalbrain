using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>The parsed topology settings: one value selects one implementation (P2-D-09).</summary>
/// <param name="Topology">The selected topology; <c>local</c> when absent and nothing else asks for more.</param>
/// <param name="Home">Where local state lives; null when <c>IntoChatOS:Home</c> is invalid.</param>
/// <param name="Issues">Every reason the host must not start.</param>
/// <param name="Warnings">Non-fatal notes, such as the deprecated <c>local-cloud</c> spelling.</param>
public sealed record TopologySettings(Topology Topology, StateHome? Home, IReadOnlyList<TopologyIssue> Issues, IReadOnlyList<string> Warnings)
{
    /// <summary>True when there are no issues.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Parses configuration without throwing; problems are returned as issues.</summary>
    public static TopologySettings Parse(IConfiguration configuration, IReadOnlyList<RetiredSetting> retired)
    {
        var (topology, issues, warnings) = TopologyValidator.Evaluate(configuration, retired);
        var all = issues.ToList();
        StateHome? home = null;
        try
        {
            home = StateHome.Resolve(configuration);
        }
        catch (InvalidOperationException exception)
        {
            all.Add(new TopologyIssue(IntoChatOSNames.HomeKey, exception.Message));
        }
        return new TopologySettings(topology ?? Topology.Local, home, all, warnings);
    }
}
