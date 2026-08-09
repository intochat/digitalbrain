using System;
using System.IO;

namespace DigitalBrain.Poc.Foundation.Tests;

internal sealed class TemporaryMsBuildRoot : IDisposable
{
    private TemporaryMsBuildRoot(string parentRoot)
    {
        ParentRoot = parentRoot;
        PocRoot = Path.Combine(parentRoot, "poc");
        SiblingPrefixRoot = Path.Combine(parentRoot, "poc-legacy");
        OutsideRoot = Path.Combine(parentRoot, "outside");
    }

    public string ParentRoot { get; }

    public string PocRoot { get; }

    public string SiblingPrefixRoot { get; }

    public string OutsideRoot { get; }

    public static TemporaryMsBuildRoot Create()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "DigitalBrain.Poc.Foundation.Tests", Guid.NewGuid().ToString("N"));
        var fixture = new TemporaryMsBuildRoot(parentRoot);
        Directory.CreateDirectory(fixture.PocRoot);
        Directory.CreateDirectory(fixture.SiblingPrefixRoot);
        Directory.CreateDirectory(fixture.OutsideRoot);
        return fixture;
    }

    public void WriteProject(string content)
    {
        File.WriteAllText(Path.Combine(PocRoot, "Boundary.csproj"), content);
    }

    public void WriteIntermediateFile(string name, string content)
    {
        var intermediateDirectory = Path.Combine(PocRoot, "obj");
        Directory.CreateDirectory(intermediateDirectory);
        File.WriteAllText(Path.Combine(intermediateDirectory, name), content);
    }

    public void WriteAuthoredFile(string relativePath, string content)
    {
        WriteFile(PocRoot, relativePath, content);
    }

    public void WriteOutsideFile(string relativePath, string content)
    {
        WriteFile(OutsideRoot, relativePath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(ParentRoot))
        {
            Directory.Delete(ParentRoot, recursive: true);
        }
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Test file has no directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, content);
    }
}
