using System.Text.Json;
using DigitalBrain.Capabilities.Http;
using DigitalBrain.Capabilities.Tools;

namespace DigitalBrain.Testing.Synapses;

/// <summary>
/// The fakes a <see cref="UnitScenario"/> binds a unit's slots to, by the <c>[Slot]</c> parameter name. No fake touches the network.
/// </summary>
public sealed class FakeSlots
{
    private readonly List<(string Slot, Action<TestOS> Bind)> _slots = [];

    /// <summary>The slot names bound so far.</summary>
    public IReadOnlyList<string> Slots => [.. _slots.Select(s => s.Slot)];

    /// <summary>Binds <paramref name="slot"/> to an <see cref="IHttpRead"/> answering every url with <paramref name="body"/>; <c>GetJson</c> parses it.</summary>
    /// <param name="slot">The slot name.</param>
    /// <param name="body">The response body for a url.</param>
    /// <returns>This instance.</returns>
    public FakeSlots Http(string slot, Func<string, string> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Slot<IHttpRead>(slot, new FakeHttp(body));
    }

    /// <summary>Binds <paramref name="slot"/> to an <see cref="ITool"/> that runs <paramref name="run"/> for a tool name and its args.</summary>
    /// <param name="slot">The slot name.</param>
    /// <param name="run">The tool's result for a tool name and its args.</param>
    /// <returns>This instance.</returns>
    public FakeSlots Tool(string slot, Func<string, JsonElement, JsonElement> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Slot<ITool>(slot, new FakeTool(run));
    }

    /// <summary>Binds <paramref name="slot"/> to <paramref name="instance"/> as the capability <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The <c>[Capability]</c> contract.</typeparam>
    /// <param name="slot">The slot name.</param>
    /// <param name="instance">The fake.</param>
    /// <returns>This instance.</returns>
    public FakeSlots Slot<T>(string slot, T instance) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(slot);
        ArgumentNullException.ThrowIfNull(instance);
        _slots.Add((slot, os => os.With(slot, instance)));
        return this;
    }

    internal void BindTo(TestOS os)
    {
        foreach (var (_, bind) in _slots) { bind(os); }
    }

    private sealed class FakeHttp(Func<string, string> body) : IHttpRead
    {
        public Task<JsonElement> GetJson(string url, CancellationToken ct) => Task.FromResult(JsonElement.Parse(body(url)));

        public Task<string> GetText(string url, CancellationToken ct) => Task.FromResult(body(url));
    }

    private sealed class FakeTool(Func<string, JsonElement, JsonElement> run) : ITool
    {
        public Task<JsonElement> Run(string tool, JsonElement args, CancellationToken ct) => Task.FromResult(run(tool, args));

        public Task<IReadOnlyList<string>> List() => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
