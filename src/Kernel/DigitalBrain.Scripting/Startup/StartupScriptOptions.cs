namespace DigitalBrain.Scripting.Startup;

internal sealed class StartupScriptOptions
{
    public const string SectionName = "DigitalBrain:Scripting";

    public string ScriptPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs");

    public string StateDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigitalBrain",
        "Scripting");
}
