using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("surface")]
internal sealed class SurfaceEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SurfaceState> state)
    : Entity<SurfaceState>(state), ISurface
{
    public async Task Open(SurfaceScene scene, int cap)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // Re-opening a scene refreshes its title and moves it to the most-recent slot.
        var scenes = (State?.Scenes ?? [])
            .Where(existing => !string.Equals(existing.SurfaceKey, scene.SurfaceKey, StringComparison.Ordinal))
            .ToList();
        scenes.Add(scene);
        while (scenes.Count > cap)
        {
            scenes.RemoveAt(0);
        }

        await SaveAsync(new SurfaceState(scenes));
    }
}
