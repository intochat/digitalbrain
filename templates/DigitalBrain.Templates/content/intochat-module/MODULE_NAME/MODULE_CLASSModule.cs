using DigitalBrain;

namespace MODULE_NAMESPACE;

/// <summary>The module. Its id is frozen once published.</summary>
[Module(Id)]
public sealed class MODULE_CLASSModule : Module
{
    /// <summary>The module id.</summary>
    public const string Id = "MODULE_ID";

    /// <summary>What the module needs from its host.</summary>
    public static ModuleManifest Manifest { get; } = new(
        Provides: [],
        Requires: [],
        Needs: [Need.State("state")],
        Settings: [Setting.Of<MODULE_CLASSOptions>()],
        Secrets: []);
}

/// <summary>The options the module binds.</summary>
public sealed class MODULE_CLASSOptions
{
    /// <summary>An example setting.</summary>
    public string Region { get; set; } = "default";
}
