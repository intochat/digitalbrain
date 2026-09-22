namespace DigitalBrain.Coding;

public sealed class CodeExecutionOptions
{
    public const string SectionName = "DigitalBrain:Coding:Execution";
    public string? Root { get; set; }
    public string DotnetPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    public string SdkVersion { get; set; } = "11.0.100-rc.1.26425.128";
    public string[] ReferencePaths { get; set; } = [];
    public string[] TestReferencePaths { get; set; } = [];
    public Dictionary<string, string[]> Modules { get; set; } = new(StringComparer.Ordinal);
    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan TestTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public long MaximumArtifactBytes { get; set; } = 1024L * 1024 * 1024;
}
