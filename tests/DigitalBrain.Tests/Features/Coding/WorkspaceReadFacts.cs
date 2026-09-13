using DigitalBrain.Coding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class WorkspaceReadFacts
{
    private static async Task<SolutionWorkspace> ReadyAsync()
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return workspace;
    }

    [Fact]
    public async Task A_skeleton_lists_types_and_member_signatures_without_bodies()
    {
        using var workspace = await ReadyAsync();
        var skeleton = await workspace.SkeletonAsync(new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal("Alpha", skeleton.Project);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "M:Alpha.Greeter.Welcome(System.String)"], skeleton.Members.Select(member => member.Id));
        Assert.Equal("public class Greeter : IWelcome", skeleton.Members[0].Signature);
        Assert.Equal("public string Greet(string name)", skeleton.Members[1].Signature);
        Assert.Equal([0, 1, 1], skeleton.Members.Select(member => member.Depth));
        Assert.Equal(5, skeleton.Members[1].Line);
    }

    [Fact]
    public async Task A_member_returns_its_declaration_with_the_body()
    {
        using var workspace = await ReadyAsync();
        var member = await workspace.MemberAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        Assert.Equal(FixtureSolutions.GreeterPath, member.Path);
        Assert.Equal(5, member.StartLine);
        Assert.Equal(5, member.EndLine);
        Assert.Equal("""public string Greet(string name) => $"Hello, {name}";""", member.Source);
    }

    [Fact]
    public async Task Callers_name_the_calling_symbol_and_the_call_site()
    {
        using var workspace = await ReadyAsync();
        var callers = await workspace.CallersAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(callers.Items);
        Assert.Equal("M:Beta.Program.Run", hit.Id);
        Assert.Equal(FixtureSolutions.ProgramPath, hit.Path);
        Assert.Equal(7, hit.Line);
        Assert.Equal("Beta", hit.Project);
        Assert.Equal(1, callers.TotalCount);
    }

    [Fact]
    public async Task Implementations_of_an_interface_member_are_the_implementing_members()
    {
        using var workspace = await ReadyAsync();
        var implementations = await workspace.ImplementationsAsync(new("M:Alpha.IWelcome.Welcome(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(implementations.Items);
        Assert.Equal("M:Alpha.Greeter.Welcome(System.String)", hit.Id);
    }

    [Fact]
    public async Task Derived_types_cross_the_project_boundary()
    {
        using var workspace = await ReadyAsync();
        var derived = await workspace.DerivedAsync(new("T:Alpha.Greeter"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(derived.Items);
        Assert.Equal("T:Beta.Shouter", hit.Id);
        Assert.Equal("Beta", hit.Project);
    }

    [Fact]
    public async Task Derived_of_an_interface_are_its_implementing_types()
    {
        using var workspace = await ReadyAsync();
        var derived = await workspace.DerivedAsync(new("T:Alpha.IWelcome"), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "T:Beta.Shouter"], derived.Items.Select(hit => hit.Id));
    }

    [Fact]
    public async Task A_skeleton_lists_fields_by_their_variables()
    {
        using var workspace = await ReadyAsync();
        var skeleton = await workspace.SkeletonAsync(new(FixtureSolutions.ShouterPath), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Beta.Shouter", "F:Beta.Shouter.Suffix", "M:Beta.Shouter.Shout(System.String)"], skeleton.Members.Select(member => member.Id));
        var field = skeleton.Members[1];
        Assert.Equal("Field", field.Kind);
        Assert.Equal("private const string Suffix = \"!\"", field.Signature);
        Assert.Equal(7, field.Line);
        Assert.Equal(1, field.Depth);
    }

    [Fact]
    public async Task A_field_member_returns_the_whole_declaration()
    {
        using var workspace = await ReadyAsync();
        var member = await workspace.MemberAsync(new("F:Beta.Shouter.Suffix"), TestContext.Current.CancellationToken);
        Assert.Equal("""private const string Suffix = "!";""", member.Source);
        Assert.Equal(7, member.StartLine);
        Assert.Equal(7, member.EndLine);
        Assert.Equal(FixtureSolutions.ShouterPath, member.Path);
    }

    [Fact]
    public async Task An_unknown_path_is_advice()
    {
        using var workspace = await ReadyAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SkeletonAsync(new("E:/fixture/Nowhere.cs"), TestContext.Current.CancellationToken));
        Assert.Contains("find-symbols", error.Message, StringComparison.Ordinal);
    }
}
