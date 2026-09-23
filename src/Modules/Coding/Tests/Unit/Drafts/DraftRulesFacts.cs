using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DraftRulesFacts
{
    [Theory]
    [InlineData("#:project ../../private.csproj\nConsole.WriteLine(1);")]
    [InlineData("#:package Bad.Package\nConsole.WriteLine(1);")]
    [InlineData("#:include other.cs\nConsole.WriteLine(1);")]
    [InlineData("#:property TargetFramework=net10.0\nConsole.WriteLine(1);")]
    public void ManagedDraftCannotControlTheBuild(string source)
        => Assert.Throws<ArgumentException>(() => DraftRules.Validate(New(source)));

    [Fact]
    public void DirectiveTextInsideAStringIsOrdinarySource()
        => DraftRules.Validate(New("Console.WriteLine(\"#:project example\");"));

    [Fact]
    public void SourceLimitCountsUtf8Bytes()
        => Assert.Throws<ArgumentException>(() => DraftRules.Validate(New(new string('界', 50_000))));

    [Fact]
    public void DuplicateModulesAreRejected()
        => Assert.Throws<ArgumentException>(() => DraftRules.Validate(New("Console.WriteLine(1);") with { ModuleIds = ["time", "time"] }));

    [Fact]
    public void TestsCannotImportBuildDependencies()
        => Assert.Throws<ArgumentException>(() => DraftRules.Validate(New("Console.WriteLine(1);") with { Tests = "#:project external.csproj" }));

    private static SaveCodeDraft New(string source) => new(0, Guid.NewGuid(), source, "public class Tests { }", []);
}