using DigitalBrain.Kernel.Foundry;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Tests.Foundry;

public class CapabilityGateTests
{
    private static CSharpCompilation CompileSnippet(string source) =>
        FoundryCompilation.Create("gatecheck", source, FoundryCompilation.DefaultReferences());

    private static IReadOnlyList<string> Inspect(string source) =>
        CapabilityGate.FindViolations(CompileSnippet(source));

    [Fact]
    public void AllowsBenignArithmetic()
    {
        var violations = Inspect("public static class M { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) => 1 + 1; }");
        Assert.Empty(violations);
    }

    [Fact]
    public void FlagsProcessStart()
    {
        var violations = Inspect("public static class M { public static object Run(System.Collections.Generic.IReadOnlyDictionary<string,object?> input) { System.Diagnostics.Process.Start(\"calc\"); return null; } }");
        Assert.Contains(violations, v => v.Contains("System.Diagnostics.Process"));
    }

    [Fact]
    public void Rejects_System_Net_By_Default()
    {
        var compilation = CompileSnippet("""
            using System.Net.Http;
            public class Probe {
                public void Run() { var c = new HttpClient(); }
            }
            """);
        var violations = CapabilityGate.FindViolations(compilation);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Rejects_System_IO_By_Default()
    {
        var compilation = CompileSnippet("""
            using System.IO;
            public class Probe {
                public void Run() { File.ReadAllText("x"); }
            }
            """);
        var violations = CapabilityGate.FindViolations(compilation);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Allows_DigitalBrain_Core_Types_And_Basic_Collections()
    {
        var compilation = CompileSnippet("""
            using System.Collections.Generic;
            using System.Linq;
            public class Probe {
                public int Run() { var list = new List<int> { 1, 2, 3 }; return list.Sum(); }
            }
            """);
        var violations = CapabilityGate.FindViolations(compilation);
        Assert.Empty(violations);
    }

}
