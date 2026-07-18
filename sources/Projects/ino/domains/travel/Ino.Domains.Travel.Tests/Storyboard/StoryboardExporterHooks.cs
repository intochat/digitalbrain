using Reqnroll;

namespace Ino.Domains.Travel.Tests.Storyboard;

[Binding]
public sealed class StoryboardExporterHooks
{
    public const string ExportEnvVar = "INO_EXPORT_STORYBOARDS";
    public const string OutputDirEnvVar = "INO_STORYBOARD_OUTPUT_DIR";
    public const string TagPrefix = "export-storyboard:";

    // From the test bin (bin/Debug/net11.0/) up to repo root, then into the
    // Flutter assets dir. Override at runtime via INO_STORYBOARD_OUTPUT_DIR.
    public const string DefaultRelativeOutputDir =
        @"..\..\..\..\..\..\clients\ino.flutter\assets\storyboards";

    private readonly ScenarioContext ctx;

    public StoryboardExporterHooks(ScenarioContext ctx)
    {
        this.ctx = ctx;
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        var tag = ctx.ScenarioInfo.Tags
            .FirstOrDefault(t => t.StartsWith(TagPrefix, StringComparison.Ordinal));
        if (tag is null) return;
        var id = tag.Substring(TagPrefix.Length);
        ctx.Set(new StoryboardRecorder(id, ctx.ScenarioInfo.Title));
    }

    [AfterScenario]
    public void AfterScenario()
    {
        if (!ctx.TryGetValue<StoryboardRecorder>(out var recorder)) return;
        if (Environment.GetEnvironmentVariable(ExportEnvVar) != "1") return;

        var outDir = Environment.GetEnvironmentVariable(OutputDirEnvVar)
            ?? Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, DefaultRelativeOutputDir));
        Directory.CreateDirectory(outDir);

        var path = Path.Combine(outDir, $"{recorder.Id}.json");
        // Force LF — Flutter assets travel cross-platform; CRLF is noise.
        var json = recorder.ToJson().Replace("\r\n", "\n");
        File.WriteAllText(path, json);
    }
}
