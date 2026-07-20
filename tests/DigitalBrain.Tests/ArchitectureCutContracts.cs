using System.Reflection;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ArchitectureCutContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] RejectedIdentifiers =
    [
        "Model" + "Tier",
        "Model" + "Providers",
        "IModel" + "CompletionService",
        "Ask" + "ModelAsync",
        "AddDigitalBrain" + "Models",
        "AddAI" + "Module",
        "Chat" + "ModelNeuron",
    ];

    [Fact(DisplayName = "the kernel exposes no model operation")]
    public void KernelExposesNoModelOperation()
    {
        var methods = typeof(Neuron)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(methods, name => name.Contains("Model", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "production source contains none of the rejected AI architecture")]
    public void ProductionSourceContainsNoRejectedAiArchitecture()
    {
        string[] roots = ["src", "modules", "hosts", "samples"];

        var violations = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => RejectedIdentifiers
                .Where(identifier => File.ReadAllText(file).Contains(identifier, StringComparison.Ordinal))
                .Select(identifier => $"{Path.GetRelativePath(RepositoryRoot, file)}: {identifier}"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
