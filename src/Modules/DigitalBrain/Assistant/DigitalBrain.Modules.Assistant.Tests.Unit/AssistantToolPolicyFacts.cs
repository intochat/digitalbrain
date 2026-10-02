using DigitalBrain.Assistant;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AssistantToolPolicyFacts
{
    [Fact]
    public void ExplicitComparisonKeepsBothSourcesWithInstalledApps()
    {
        var tools = AssistantToolPolicy.SelectTools([], ["run_customer_researcher"], message: "Compare Postgres and Supabase");
        Assert.Contains("postgres_schema", tools);
        Assert.Contains("show_postgres_query_table", tools);
        Assert.Contains("supabase_schema", tools);
        Assert.Contains("show_supabase_query_table", tools);
        Assert.True(tools.Count <= AssistantToolPolicy.MaxDefaultTools);
    }

    [Theory]
    [InlineData("Show data from postgres", "postgres_schema", "supabase_schema")]
    [InlineData("Show data from PostgreSQL", "postgres_schema", "supabase_schema")]
    [InlineData("Show data from Supabase", "supabase_schema", "postgres_schema")]
    public void ExplicitDatabaseExcludesTheOtherDatabaseTools(string message, string expected, string forbidden)
    {
        var tools = AssistantToolPolicy.SelectTools([], message: message);
        Assert.Contains(expected, tools);
        Assert.DoesNotContain(forbidden, tools);
    }

    [Fact]
    public void UnavailablePostgresDoesNotSubstituteSupabase()
    {
        var tools = AssistantToolPolicy.ForDatabase(["supabase_schema", "show_supabase_query_table"], "Show data from Postgres");
        Assert.Empty(tools);
    }
    [Fact]
    public void DefaultAllowlistIsTheEightProductToolsAndExcludesCSharpTools()
    {
        var selected = AssistantToolPolicy.SelectTools([]);
        Assert.Equal(
            ["table_read", "table_refine", "show_form", "show_view",
             "supabase_schema", "show_supabase_query_table", "postgres_schema", "show_postgres_query_table"],
            selected);
        Assert.Equal(8, AssistantToolPolicy.MaxDefaultTools);
    }

    [Fact]
    public void RelevantAppToolsJoinTheCoreAndNoneOfThemExceedTheCap()
    {
        var selected = AssistantToolPolicy.SelectTools([],
            ["propose_app", "run_leadgenerator"]);
        Assert.True(selected.Count <= AssistantToolPolicy.MaxDefaultTools);
        Assert.DoesNotContain("find_capability", selected);
        Assert.Contains("propose_app", selected);
        Assert.Contains("run_leadgenerator", selected);
        Assert.DoesNotContain("supabase_schema", selected);
    }

    [Fact]
    public void ATableIntentKeepsTheGenericTableToolsWhenAnAppIsRelevant()
    {
        var selected = AssistantToolPolicy.SelectTools([],
            ["propose_app"], tableIntent: true);
        Assert.Contains("supabase_schema", selected);
        Assert.Contains("show_supabase_query_table", selected);
    }

    [Fact]
    public void ManyRelevantAppToolsStillFitTheCap()
    {
        var selected = AssistantToolPolicy.SelectTools([],
            ["propose_app", "run_leadgenerator", "plan_background_removal", "run_background_removal", "extra_app_tool"]);
        Assert.Equal(AssistantToolPolicy.MaxDefaultTools, selected.Count);
        Assert.Equal(selected.Count, selected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RegisteredAuthoringToolsJoinTheProductTools()
    {
        string[] developerTools = ["csharp_contracts", "csharp_write", "csharp_run"];
        var selected = AssistantToolPolicy.SelectTools(developerTools);
        Assert.Equal(AssistantToolPolicy.ProductTools.Concat(developerTools), selected);
    }

}
