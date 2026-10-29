namespace DigitalBrain;

/// <summary>
/// The unit of composition. A module class is marked <c>[Module(id)]</c> and declares
/// <c>public static ModuleManifest Manifest</c>; <see cref="ModuleManifest.Of(Type)"/> reads both.
/// </summary>
public abstract class Module
{
    /// <summary>Creates the module.</summary>
    protected Module()
    {
    }
}
