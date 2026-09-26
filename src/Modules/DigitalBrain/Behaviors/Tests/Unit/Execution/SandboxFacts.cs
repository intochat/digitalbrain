using DigitalBrain.Behavior;
using DigitalBrain.Core;

namespace DigitalBrain.Modules.Behavior.Tests.Unit.Execution;

public sealed class SandboxFacts
{
    [Fact]
    public void LaunchIsReadOnlyAndDoesNotMountTheEngine()
    {
        var arguments = BehaviorSandbox.Arguments(new BehaviorSandboxRequest(
            BehaviorSandbox.ImageName,
            "digitalbrain-behavior-abc",
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "artifact")),
            "Behavior.dll",
            41000,
            new Dictionary<string, string> { ["Gateways"] = "gwy.tcp://127.0.0.1:30000/0", ["ClusterId"] = "local" }));

        var text = string.Join('\n', arguments);
        Assert.Contains("--read-only", arguments);
        Assert.Contains("--cap-drop=ALL", arguments);
        Assert.Contains("--security-opt=no-new-privileges", arguments);
        Assert.Contains("127.0.0.1:41000:" + BehaviorSandboxControl.Port, arguments);
        Assert.Contains("Behavior.dll", arguments);
        Assert.Equal(BehaviorSandbox.ImageName, arguments[^2]);
        Assert.Contains("host.docker.internal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("docker.sock", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--network=host", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--privileged", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEntryThatEscapesTheArtifact()
    {
        Assert.Throws<ArgumentException>(() => BehaviorSandbox.Arguments(new BehaviorSandboxRequest(
            BehaviorSandbox.ImageName, "digitalbrain-behavior-abc", Path.GetFullPath("artifact"),
            "../Behavior.dll", 1, new Dictionary<string, string>())));
    }
}