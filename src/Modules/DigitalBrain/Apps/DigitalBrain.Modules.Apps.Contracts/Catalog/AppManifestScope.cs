namespace DigitalBrain.Apps;

// Where an app's manifest came from: committed first-party/platform manifests are global and visible
// to every workspace, while a saved or workspace-installed app belongs to the workspace that owns it.
public enum AppManifestScope
{
    Global = 0,
    Workspace = 1,
}
