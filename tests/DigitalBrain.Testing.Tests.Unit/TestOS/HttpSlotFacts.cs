using System.Text.Json;
using DigitalBrain.Capabilities.Http;
using DigitalBrain.Rules;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// A unit reaches the outside through an http@1 slot; the slot binds to the connection named in the source's bindings.
public sealed class HttpSlotFacts
{
    private sealed class FakeHttp(string body) : IHttpRead
    {
        public Task<JsonElement> GetJson(string url, CancellationToken ct) => Task.FromResult(JsonElement.Parse("{}"));

        public Task<string> GetText(string url, CancellationToken ct) => Task.FromResult(body);
    }

    // The gate rejects with what the bound connection returned.
    private static readonly string ReadsHttp = HsSource.Budget
        .Replace("namespace Me.HostBudget;", "using DigitalBrain.Capabilities.Http;\n\nnamespace Me.HostBudget;", StringComparison.Ordinal)
        .Replace("public static Check WeeklyCap(Charging s, WeekSpend t)", "public static async Task<Check> WeeklyCap(Charging s, WeekSpend t, [Slot] IHttpRead Http)", StringComparison.Ordinal)
        .Replace("t.Spent + s.Amount > 300m", "t.Spent + s.Amount > 0m", StringComparison.Ordinal)
        .Replace("Check.Reject(\"weekly cap of $300\")", "Check.Reject($\"http {await Http.GetText(\"https://api.example.org/x\", CancellationToken.None)}\")", StringComparison.Ordinal);

    [Fact]
    public async Task ASlotOfTypeIHttpReadBindsToAnHttpConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var os = OS.Local().Register<HsPayments>().With<IHttpRead>("feed", new FakeHttp("hello"));
        var brain = NeuronId.Parse("hh/x/y").Brain;
        var bindings = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["hs.week"] = new Dictionary<string, string> { ["Http"] = "feed" } };

        var outcome = await os.Synapses.Deploy(brain, HsSource.Of(ReadsHttp) with { Bindings = bindings }, new Dictionary<string, IReadOnlyDictionary<string, string>>(), ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => os.Get<IHsPayments>(NeuronId.Parse("hh/hs.payments/p")).Charge(1m));
        Assert.Equal("http hello", e.Rejected.Messages[0]);
    }
}
