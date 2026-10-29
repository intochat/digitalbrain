namespace DigitalBrain.Tests.Unit;

public sealed class PublicApiTextFacts
{
    public static TheoryData<string> Files()
    {
        var root = Root();
        var data = new TheoryData<string>();
        foreach (var project in new[] { "DigitalBrain", "DigitalBrain.UI" })
        {
            data.Add(Path.Combine(root, "src", project, "PublicAPI.Shipped.txt"));
            data.Add(Path.Combine(root, "src", project, "PublicAPI.Unshipped.txt"));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Public_api_has_no_runtime_or_hosting_text(string file)
    {
        Assert.True(File.Exists(file), file);
        var text = File.ReadAllText(file);

        foreach (var banned in new[] { "Orleans", "ISiloBuilder", "Microsoft.AspNetCore" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Public_api_is_snapshotted()
    {
        Assert.Contains("DigitalBrain.NeuronId", File.ReadAllText(Path.Combine(Root(), "src", "DigitalBrain", "PublicAPI.Unshipped.txt")), StringComparison.Ordinal);
        Assert.Contains("DigitalBrain.UI.View", File.ReadAllText(Path.Combine(Root(), "src", "DigitalBrain.UI", "PublicAPI.Unshipped.txt")), StringComparison.Ordinal);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
