using System.Text.RegularExpressions;

namespace IntoChat.Tests.Unit.Operations;

public sealed class HostedSdkModuleFacts
{
    [Fact]
    public void EveryKernelModuleTheHostedImageBakesStillExists()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { directory = directory.Parent; }
        var dockerfile = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Applications", "IntoChat", "IntoChat", "Dockerfile"));

        var sdkModules = Regex.Matches(dockerfile, @"DigitalBrain__Modules__\d+=""(DigitalBrain\.(?:Sdk|Platform)\.[^""]+)""")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Contains(sdkModules, module => module.StartsWith("DigitalBrain.Platform.Integrations.IntegrationsModule", StringComparison.Ordinal));
        Assert.All(sdkModules, module => Assert.NotNull(Type.GetType(module)));
    }
}
