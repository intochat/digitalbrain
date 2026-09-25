using DigitalBrain.CSharpExpert;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodingProfileFacts
{
    [Fact]
    public async Task ProfileExposesTheSpecDefaults()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpExpertModule>().StartAsync(ct);
        var profile = await brain.Get<ICodingProfile>("profile-defaults").Read();
        Assert.Equal("planner", profile.PlannerAgentId);
        Assert.Equal("implementer", profile.ImplementerAgentId);
        Assert.Equal(3, profile.MaxFixAttempts);
        Assert.True(profile.BuildAndTestEachStep);
        Assert.Contains("self-explanatory", profile.ReviewRules);
        Assert.Equal("latest", profile.NuGetPolicy);
    }

    [Fact]
    public async Task WrittenProfileRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpExpertModule>().StartAsync(ct);
        var profile = brain.Get<ICodingProfile>("profile-custom");
        await profile.Write(CodingProfile.Default with { PlannerAgentId = "architect", MaxFixAttempts = 5 });
        var stored = await profile.Read();
        Assert.Equal("architect", stored.PlannerAgentId);
        Assert.Equal(5, stored.MaxFixAttempts);
    }
}
