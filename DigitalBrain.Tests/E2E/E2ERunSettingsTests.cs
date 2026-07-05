using System.Xml.Linq;

namespace DigitalBrain.Tests.E2E;

public class E2ERunSettingsTests
{
    private static string RunSettingsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "e2e.runsettings"));

    [Fact]
    public void Runsettings_file_exists_at_the_repo_root()
    {
        Assert.True(File.Exists(RunSettingsPath), $"Expected {RunSettingsPath} to exist.");
    }

    [Fact]
    public void Runsettings_declares_the_real_stack_e2e_opt_in()
    {
        var doc = XDocument.Load(RunSettingsPath);
        var envVars = doc.Root?.Element("RunConfiguration")?.Element("EnvironmentVariables");

        Assert.NotNull(envVars);
        Assert.Equal("true", envVars!.Elements().FirstOrDefault(e => e.Name == "RUN_REAL_STACK_E2E")?.Value);
    }
}
