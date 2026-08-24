using System.Net;
using System.Net.Http.Json;
using DigitalBrain.SmartPrompt;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class BehaviorSurfaceTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Eight_seeded_behaviors_are_green_and_fake_runnable()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var behaviors = await http.GetFromJsonAsync<List<BehaviorSummary>>(
            "/behaviors", TestContext.Current.CancellationToken);
        Assert.NotNull(behaviors);
        Assert.Equal(8, behaviors.Count);
        Assert.All(behaviors, item =>
        {
            Assert.True(item.Active, item.Name);
            Assert.True(item.LastTest?.AllGreen, item.Name);
        });

        foreach (var behavior in behaviors)
        {
            var response = await http.PostAsync(
                $"/behaviors/{behavior.Name}/fake", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Local_Gemma_generates_only_compilable_Reqnroll_source()
    {
        using var http = fixture.CreateHttpClient("kernel");
        var response = await http.PostAsJsonAsync("/behaviors/generate",
            new { request = "Create a behavior that notifies me when Bitcoin is above 90000." },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = await response.Content.ReadFromJsonAsync<BehaviorGeneration>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(generated);
        Assert.Equal("gemma4:12b", generated.Model);
        Assert.True(generated.Compilation.Success,
            string.Join(Environment.NewLine, generated.Compilation.Diagnostics.Select(x => x.Message)));
    }
}
