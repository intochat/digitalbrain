namespace DigitalBrain.Os;

public sealed class SimulationCatalog
{
    public sealed record CompiledScenario(string Tags, string Feature, string Scenario, string ClassName, string MethodName, string SourcePath);
    public sealed record CapsuleScenario(string ExperienceId, string ScenarioName, string Source);

    public List<CompiledScenario> Compiled { get; } = new();
    public List<CapsuleScenario> Capsules { get; } = new();

    public SimulationCatalog()
    {
        LoadCompiledFromFeatures();
        LoadCapsulesFromMarketplace();
    }

    private void LoadCompiledFromFeatures()
    {
        var featuresRoot = "src/DigitalBrain.Os.Tests";
        foreach (var featureFile in new[] { "DistributionDynamicHandlers.feature", "GoogleAuthU4.feature" })
        {
            var path = Path.Combine(featuresRoot, featureFile);
            if (!File.Exists(path)) continue;
            var content = File.ReadAllText(path);
            var tags = new List<string>();
            string currentFeature = "";
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("@"))
                {
                    tags.Add(trimmed.TrimStart('@'));
                }
                else if (trimmed.StartsWith("Feature:"))
                {
                    currentFeature = trimmed.Substring(8).Trim();
                }
                else if (trimmed.StartsWith("Scenario:") || trimmed.StartsWith("Scenario Outline:"))
                {
                    var scenario = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    if (currentFeature.Length > 0)
                    {
                        var classHint = currentFeature.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "");
                        var methodHint = scenario.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(",", "");
                        var tagStr = string.Join(",", tags);
                        Compiled.Add(new CompiledScenario(tagStr, currentFeature, scenario, classHint, methodHint, featureFile));
                    }
                    if (!trimmed.StartsWith("Scenario Outline:"))
                        tags.Clear(); // scenario level tags would be after feature, but for simplicity reset on scenario
                }
            }
        }
    }

    private void LoadCapsulesFromMarketplace()
    {
        var market = "pa-files/marketplace";
        if (!Directory.Exists(market)) return;
        foreach (var dir in Directory.GetDirectories(market))
        {
            var id = Path.GetFileName(dir);
            var inoFiles = Directory.GetFiles(dir, "*.ino", SearchOption.AllDirectories);
            foreach (var ino in inoFiles)
            {
                var text = File.ReadAllText(ino);
                if (text.Contains("scenario:") || text.Contains("Scenario:"))
                {
                    Capsules.Add(new CapsuleScenario(id, Path.GetFileNameWithoutExtension(ino), ino));
                }
            }
        }
    }

    public IEnumerable<object> Filter(string filter)
    {
        var f = (filter ?? "").Trim().ToLowerInvariant();
        if (f.StartsWith("tag:"))
        {
            var t = f.Substring(4);
            return Compiled.Where(c => c.Tags.ToLowerInvariant().Contains(t)).Cast<object>().Concat(
                Capsules.Where(c => c.ExperienceId.ToLowerInvariant().Contains(t)));
        }
        if (f.StartsWith("scenario:"))
        {
            var s = f.Substring(9);
            return Compiled.Where(c => c.Scenario.ToLowerInvariant().Contains(s)).Cast<object>().Concat(
                Capsules.Where(c => c.ScenarioName.ToLowerInvariant().Contains(s)));
        }
        if (f.StartsWith("ino:"))
        {
            var i = f.Substring(4);
            return Capsules.Where(c => c.ExperienceId.ToLowerInvariant().Contains(i)).Cast<object>();
        }
        // bare or neuron/synapse thin: contains across
        return Compiled.Where(c =>
            c.Tags.ToLowerInvariant().Contains(f) ||
            c.Feature.ToLowerInvariant().Contains(f) ||
            c.Scenario.ToLowerInvariant().Contains(f) ||
            c.ClassName.ToLowerInvariant().Contains(f)
        ).Cast<object>().Concat(
            Capsules.Where(c =>
                c.ExperienceId.ToLowerInvariant().Contains(f) ||
                c.ScenarioName.ToLowerInvariant().Contains(f)
            )
        );
    }

    public string FormatList()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Compiled scenarios ===");
        foreach (var c in Compiled)
            sb.AppendLine($"  [{c.Tags}] {c.Feature} / {c.Scenario} (class:{c.ClassName} method:{c.MethodName})");
        sb.AppendLine("=== Capsule scenarios ===");
        foreach (var c in Capsules)
            sb.AppendLine($"  ino:{c.ExperienceId} / {c.ScenarioName}");
        return sb.ToString();
    }
}