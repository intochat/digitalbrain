using System.Text.Json;
using DigitalBrain.Flutter.Aspire.Hosting;

namespace DigitalBrain.Aspire.Hosting.Tests.Unit.Flutter;

public sealed class ShellContainerFacts
{
    [Fact]
    public void ServedConfigCarriesTheHostVariablesTheShellResolves()
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(
            ShellServedConfig.Payload("http://localhost:5000", "desk", "main"));

        Assert.NotNull(payload);
        Assert.Equal("http://localhost:5000", payload[ShellNames.UIBaseEnvironmentVariable]);
        Assert.Equal("desk", payload[ShellNames.ShellEnvironmentVariable]);
        Assert.Equal("main", payload[ShellNames.ChatEnvironmentVariable]);
    }

    [Fact]
    public void TheShellPackageShipsTheContainerDockerfileInsideThePubWorkspace()
    {
        var packageRoot = ShellHostingExtensions.ResolveFlutterWorkingDirectory(AppContext.BaseDirectory, configured: null);
        var shellDirectory = FlutterHostLaunch.ResolveWebPackageDirectory(packageRoot);

        Assert.NotNull(shellDirectory);
        Assert.True(File.Exists(Path.Combine(shellDirectory, "Dockerfile")),
            $"No Dockerfile at '{shellDirectory}' — the WebContainer host cannot build the shell image.");
        var workspaceRoot = Path.GetFullPath(Path.Combine(shellDirectory, ".."));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "pubspec.yaml")),
            $"'{workspaceRoot}' is not the pub workspace root the docker build context expects.");
    }
}
