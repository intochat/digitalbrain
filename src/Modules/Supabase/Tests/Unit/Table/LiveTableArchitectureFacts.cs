using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit.Table;

// Proves C09/P1.3 consolidation: one live-table compiler/policy/guard/type-map cluster remains
// (Supabase), the other three baseline mechanisms are gone, and the retained UI-kit ITable adapter
// is ADR-fenced rather than a second data source.
public sealed class LiveTableArchitectureFacts
{
    [Fact]
    public void ExactlyOneLiveTableClusterRemainsAndTheItableAdapterIsAdrFenced()
    {
        var root = RepositoryRoot();
        var sources = SourceFiles(Path.Combine(root, "src"));

        AssertEmpty(sources, "IClickHouseTable.cs");
        AssertEmpty(sources, "ClickHouseTableNeuron.cs");
        AssertEmpty(sources, "ClickHouseTablePolicy.cs");
        AssertEmpty(sources, "ClickHouseTableState.cs");
        AssertEmpty(sources, "ClickHouseTableView.cs");
        AssertEmpty(sources, "TableRendered.cs");
        AssertEmpty(sources, "QueryWindowOperation.cs");
        AssertEmpty(sources, "QueryWindowOperationNeuron.cs");
        AssertEmpty(sources, "QueryWindowContracts.cs");

        // One live-table query compiler and one table policy.
        Assert.Single(sources, file => Name(file) == "QueryPlanCompiler.cs");
        Assert.Single(sources, file => Name(file) == "SupabaseTablePolicy.cs");
        Assert.DoesNotContain(sources, file => Name(file).EndsWith("TablePolicy.cs", StringComparison.Ordinal)
            && Name(file) != "SupabaseTablePolicy.cs");

        // Supabase owns the single guard and type map for the live-table cluster; the retained
        // ITable UI-kit adapter has none of its own (it stores caller-supplied rows in grain state).
        Assert.Single(sources, file => Name(file) == "SupabaseQueryGuard.cs");
        Assert.Single(sources, file => Name(file) == "SupabaseTypeMap.cs");
        Assert.DoesNotContain(sources, file => Name(file).Contains("TableTypeMap.cs", StringComparison.Ordinal));

        // The retained UI-kit table adapter is present but must stay a declared adapter: no SQL
        // compiler, policy, guard or type map lives beside it.
        var itable = Path.Combine(root, "src", "Modules", "Google", "Flutter", "Contracts", "Table", "ITable.cs");
        Assert.True(File.Exists(itable), $"Expected the ADR-fenced ITable adapter at {itable}.");
        var flutterFiles = SourceFiles(Path.Combine(root, "src", "Modules", "Google", "Flutter"));
        Assert.DoesNotContain(flutterFiles, file => Name(file) is "QueryPlanCompiler.cs" or "SupabaseQueryGuard.cs" or "SupabaseTypeMap.cs");
        Assert.DoesNotContain(flutterFiles, file => Name(file) == "ITablePolicy.cs");

        var adr = Path.Combine(root, "docs", "product", "decisions", "0002-one-live-table-contract.md");
        Assert.True(File.Exists(adr), $"Expected ADR 0002 at {adr}.");
    }

    private static string Name(string path) => Path.GetFileName(path);

    private static void AssertEmpty(string[] sources, string fileName)
        => Assert.DoesNotContain(sources, file => Name(file) == fileName);

    private static string[] SourceFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .ToArray()
            : [];

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("Could not locate the repository root (DigitalBrain.slnx).");
    }
}