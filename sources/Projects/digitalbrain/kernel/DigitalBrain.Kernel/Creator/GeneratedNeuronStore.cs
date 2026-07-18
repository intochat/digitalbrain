using System.Text.Json;
using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Creator;

// DynamicNeuronSpec has no Manifest property; we derive a minimal manifest
// from the spec's own metadata for developer visibility in the on-disk mirror.
internal sealed class GeneratedNeuronStore(
    IDynamicMirrorPath path,
    ILogger<GeneratedNeuronStore> log) : IGeneratedNeuronStore
{
    public async Task WriteAsync(DynamicNeuronSpec spec, string stepsCode)
    {
        var dir = path.For(spec.Id);
        Directory.CreateDirectory(dir);
        var slug = spec.Id.Value.Split('/').Last();

        var manifest = new
        {
            id = spec.Id.Value,
            createdAtUtc = spec.CreatedAt.UtcDateTime,
            status = spec.Status.ToString(),
        };

        await File.WriteAllTextAsync(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(dir, $"{slug}.feature"), spec.FeatureText);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{slug}.cs"),       spec.RoslynScript);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{slug}.Steps.cs"), stepsCode);

        log.LogInformation("Mirrored generated neuron {Id} to {Dir}", spec.Id.Value, dir);
    }
}
