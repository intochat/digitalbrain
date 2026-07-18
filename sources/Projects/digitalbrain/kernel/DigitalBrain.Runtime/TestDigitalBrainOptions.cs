namespace DigitalBrain.Runtime;

public sealed class TestDigitalBrainOptions
{
    public Dictionary<string, string?> EnvironmentOverrides { get; } = new(StringComparer.Ordinal);

    public TestDigitalBrainOptions WithClusterId(string clusterId)
    {
        EnvironmentOverrides["ORLEANS_CLUSTER_ID"] = clusterId;
        return this;
    }

    public TestDigitalBrainOptions WithMockedLlm()
    {
        EnvironmentOverrides["DigitalBrain__Ai__UseMockClient"] = "true";
        EnvironmentOverrides["DigitalBrain:Ai:UseMockClient"] = "true";
        return this;
    }

    public TestDigitalBrainOptions WithStubbedGoogle(params string[] usersWithoutTokens)
    {
        EnvironmentOverrides["DigitalBrain__Google__UseStubServices"] = "true";
        EnvironmentOverrides["DigitalBrain:Google:UseStubServices"] = "true";
        if (usersWithoutTokens.Length > 0)
        {
            var joined = string.Join(',', usersWithoutTokens);
            EnvironmentOverrides["DigitalBrain__Google__Stub__UsersWithoutTokens"] = joined;
            EnvironmentOverrides["DigitalBrain:Google:Stub:UsersWithoutTokens"] = joined;
        }
        return this;
    }

    public TestDigitalBrainOptions WithEnvironmentOverride(string key, string value)
    {
        EnvironmentOverrides[key] = value;
        return this;
    }

    public bool ParallelIsolation { get; set; } = true;

    public TestDigitalBrainOptions Snapshot()
    {
        var snapshot = new TestDigitalBrainOptions { ParallelIsolation = this.ParallelIsolation };
        foreach (var (key, value) in EnvironmentOverrides)
            snapshot.EnvironmentOverrides[key] = value;
        return snapshot;
    }
}
