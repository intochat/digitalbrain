using FluentAssertions;
using Ino.NeuronTesting;
using Reqnroll;

namespace Ino.NeuronTesting.Bdd;

// Reusable Reqnroll bindings that speak the neuron/synapse vocabulary.
// Test authors write only .feature files — these step methods translate
// each Gherkin phrase into a NeuronSession call.
//
// DI wiring (slice 2): NeuronSession must be registered in the Reqnroll
// IObjectContainer per-scenario via a [BeforeScenario] hook in each test
// project that calls IObjectContainer.RegisterInstanceAs(session). That
// hook lands in slice 2 when real Gherkin scenarios start running.
[Binding]
public sealed class NeuronSteps
{
    readonly NeuronSession _s;
#pragma warning disable CS0414 // reserved for slice 2 tag-gated hooks
    readonly ScenarioContext _ctx;
#pragma warning restore CS0414

    public NeuronSteps(NeuronSession session, ScenarioContext ctx)
    {
        _s = session;
        _ctx = ctx;
    }

    [Given(@"the user says ""(.*)""")]
    [When(@"the user says ""(.*)""")]
    public Task UserSays(string prompt) => _s.Chat(prompt);

    [Given(@"the user said ""(.*)""")]
    public Task UserSaid(string prompt) => _s.Chat(prompt);

    [Given(@"the user fired ""([^""]+)"" with (.+)")]
    [When(@"the user fires ""([^""]+)"" with (.+)")]
    public Task UserFires(string eventName, string args) =>
        _s.Fire(eventName, KeyValueParser.Parse(args));

    [When(@"the user fires ""([^""]+)""$")]
    public Task UserFiresNoArgs(string eventName) =>
        _s.Fire(eventName, new Dictionary<string, string>());

    [When(@"a ""(\w+)"" synapse arrives with (.+)")]
    public Task SynapseArrives(string synapseType, string args) =>
        _s.Fire(synapseType, KeyValueParser.Parse(args));

    [Then(@"the user sees a card with content type ""(.*)""")]
    public async Task SeesContentType(string contentType)
    {
        var frame = await _s.WaitForRfw(contentType);
        frame.ContentType.Should().Contain(contentType);
    }

    [Then(@"the card includes widget ""(.+)""")]
    [Then(@"the card includes widgets ""(.+)""")]
    public void CardIncludesWidgets(string widgetsCsv)
    {
        var widgets = widgetsCsv.Split(',', StringSplitOptions.TrimEntries)
            .Select(w => w.Trim('"'))
            .ToArray();
        // Callers reach this step only when they expect RFW to be present.
        _s.Last.Rfw!.ContainsWidgets(widgets).Should().BeTrue();
    }

    [Then(@"the card data includes ""(.+)""")]
    public void CardDataIncludes(string substringsCsv)
    {
        var raw = _s.Last.Rfw!.Data.GetRawText();
        foreach (var fragment in substringsCsv.Split(',', StringSplitOptions.TrimEntries)
                                              .Select(f => f.Trim('"')))
            raw.Should().Contain(fragment);
    }

    [Then(@"the assistant reply contains ""(.*)""")]
    public void ReplyContains(string fragment) =>
        _s.Last.Reply.Should().Contain(fragment);

    [Then(@"(\w+) emitted a ""(\w+)"" synapse with (.+)")]
    public async Task NeuronEmittedSynapse(string _, string synapseType, string args)
    {
        var fire = await _s.WaitForSynapse(synapseType);
        var expected = KeyValueParser.Parse(args);
        foreach (var kv in expected)
            fire.Args.Should().ContainKey(kv.Key).WhoseValue.Should().Be(kv.Value);
    }
}
