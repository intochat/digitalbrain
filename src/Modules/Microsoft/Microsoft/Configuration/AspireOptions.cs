namespace DigitalBrain.Microsoft;

public sealed class AspireOptions
{
    public const string SectionName = MicrosoftModule.AspireConfigurationRoot;
    public string? ProjectPath { get; set; }
    public string ApplicationName { get; set; } = "DigitalBrain";
    public string Command { get; set; } = "aspire";
    public string? Alias { get; set; }

    internal AspireConnectionSettings? CreateSettings()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return null;
        }
        var project = Path.GetFullPath(ProjectPath);
        if (!File.Exists(project))
        {
            throw new InvalidOperationException("The configured Aspire AppHost project does not exist.");
        }
        return new(project, ApplicationName, Command);
    }
}
