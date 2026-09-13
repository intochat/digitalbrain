using Microsoft.CodeAnalysis;

namespace DigitalBrain.Tests.Coding;

// The two-project fixture written to a temporary folder, so commits and the watcher touch real files.
internal sealed class DiskFixture : IDisposable
{
    private DiskFixture(string root) => Root = root;

    public string Root { get; }

    public string SolutionPath => Root + "/Fixture.slnx";

    public string GreeterPath => Root + "/Alpha/Greeter.cs";

    public string ProgramPath => Root + "/Beta/Program.cs";

    public string UnusedPath => Root + "/Beta/Unused.cs";

    public static DiskFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-coding", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        foreach (var (path, source) in FixtureSolutions.Documents(root))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }

        File.WriteAllText(Path.Combine(root, "Fixture.slnx"), "<Solution />");
        return new DiskFixture(root);
    }

    public Workspace Open() => FixtureSolutions.Build(Root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
