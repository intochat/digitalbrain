using System.Reflection;

namespace DigitalBrain.Kernel.Visualization;

public sealed class NeuronFeatureLoader(ILogger<NeuronFeatureLoader> logger)
    : IHostedService, INeuronFeatureLoader
{
    readonly Dictionary<string, (string Text, string SourceFile)> features = new(StringComparer.Ordinal);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LoadFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        logger.LogInformation("NeuronFeatureLoader indexed {Count} .feature resources.", features.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public (string Text, string SourceFile)? GetFeature(string neuronTypeFullName)
        => features.TryGetValue(neuronTypeFullName, out var entry) ? entry : null;

    public void LoadFromAssemblies(IEnumerable<Assembly> assemblies)
    {
        const string Suffix = ".feature";
        foreach (var asm in assemblies)
        {
            string[] resourceNames;
            try { resourceNames = asm.GetManifestResourceNames(); }
            catch { continue; }

            foreach (var name in resourceNames)
            {
                if (!name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                var key = name[..^Suffix.Length];
                features[key] = (text, name);
            }
        }
    }
}
