using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class ReactionTableFacts
{
    public record BaseSignal : Signal;

    public record SpecificSignal : BaseSignal;

    [Signal("custom.alias")]
    public record AliasedSignal : Signal;

    public record UnhandledSignal : Signal;

    public class ReactionNeuron : Neuron
    {
        public bool HandledBase { get; private set; }
        public bool HandledSpecific { get; private set; }
        public bool HandledWithToken { get; private set; }
        public CancellationToken PassedToken { get; private set; }

        [On]
        public void OnBase(BaseSignal signal) => HandledBase = true;

        [On]
        public void OnSpecific(SpecificSignal signal) => HandledSpecific = true;

        [On]
        public Task OnAliased(AliasedSignal signal, CancellationToken ct)
        {
            HandledWithToken = true;
            PassedToken = ct;
            return Task.CompletedTask;
        }

        public int InvalidCalls { get; private set; }

        // Invalid signature: 2nd parameter is not a CancellationToken. Must be ignored!
        [On]
        public void OnInvalid(UnhandledSignal signal, int extraParameter)
        {
            InvalidCalls++;
            throw new InvalidOperationException("Must never be called");
        }
    }

    public class ThrowingNeuron : Neuron
    {
        public int ThrowsCount { get; private set; }

        [On]
        public void OnSignal(BaseSignal signal)
        {
            ThrowsCount++;
            throw new FormatException("Original domain error");
        }
    }

    [Fact]
    public void Find_selects_most_specific_parameter_type()
    {
        var method = ReactionTable.Find(typeof(ReactionNeuron), typeof(SpecificSignal));
        Assert.NotNull(method);
        Assert.Equal(nameof(ReactionNeuron.OnSpecific), method.Name);

        var baseMethod = ReactionTable.Find(typeof(ReactionNeuron), typeof(BaseSignal));
        Assert.NotNull(baseMethod);
        Assert.Equal(nameof(ReactionNeuron.OnBase), baseMethod.Name);
    }

    [Fact]
    public void Find_ignores_handlers_with_invalid_arity_or_non_token_second_parameter()
    {
        var method = ReactionTable.Find(typeof(ReactionNeuron), typeof(UnhandledSignal));
        Assert.Null(method);
    }

    [Fact]
    public async Task React_invokes_reaction_and_returns_true_or_returns_false_when_unhandled()
    {
        var neuron = new ReactionNeuron();
        using var cts = new CancellationTokenSource();

        var handled = await ReactionTable.React(neuron, new AliasedSignal(), cts.Token);
        Assert.True(handled);
        Assert.True(neuron.HandledWithToken);
        Assert.Equal(cts.Token, neuron.PassedToken);

        var unhandled = await ReactionTable.React(neuron, new UnhandledSignal(), CancellationToken.None);
        Assert.False(unhandled);
    }

    [Fact]
    public async Task Invoke_unwraps_TargetInvocationException_to_surface_original_exception()
    {
        var neuron = new ThrowingNeuron();
        var signal = new BaseSignal();
        var method = ReactionTable.Find(typeof(ThrowingNeuron), typeof(BaseSignal))!;

        var ex = await Assert.ThrowsAsync<FormatException>(() =>
            ReactionTable.Invoke(method, neuron, signal, CancellationToken.None));

        Assert.Equal("Original domain error", ex.Message);
    }

    [Fact]
    public void ResolveAlias_and_MatchesAlias_handle_custom_and_default_aliases()
    {
        Assert.Equal("custom.alias", ReactionTable.ResolveAlias(typeof(AliasedSignal)));
        Assert.Equal(nameof(SpecificSignal), ReactionTable.ResolveAlias(typeof(SpecificSignal)));

        var aliasedInstance = new AliasedSignal();
        Assert.True(ReactionTable.MatchesAlias("custom.alias", aliasedInstance));
        Assert.False(ReactionTable.MatchesAlias("other.alias", aliasedInstance));
    }

    [Fact]
    public void Wants_evaluates_exact_type_alias_and_type_alias_matches()
    {
        var aliasedSignal = new AliasedSignal();
        var specificSignal = new SpecificSignal();

        // 1. Matches by exact type
        Assert.True(ReactionTable.Wants([typeof(SpecificSignal)], [], specificSignal));

        // 2. Matches by string alias
        Assert.True(ReactionTable.Wants([], ["custom.alias"], aliasedSignal));

        // 3. Matches by type whose [Signal] alias equals the incoming signal alias
        Assert.True(ReactionTable.Wants([typeof(AliasedSignal)], [], aliasedSignal));

        // 4. No match
        Assert.False(ReactionTable.Wants([typeof(SpecificSignal)], ["other"], aliasedSignal));
    }

    [Fact]
    public void SortSynapses_orders_deterministically_by_from_to_and_signal()
    {
        var items = new (string From, string To, string Sig)[]
        {
            ("b/n2", "b/n1", "z"),
            ("b/n1", "b/n2", "b"),
            ("b/n1", "b/n2", "a"),
            ("a/n1", "b/n1", "c"),
        };

        var sorted = ReactionTable.SortSynapses(items, i => i.From, i => i.To, i => i.Sig);

        Assert.Equal("a/n1", sorted[0].From);
        Assert.Equal("b/n1", sorted[1].From);
        Assert.Equal("a", sorted[1].Sig);
        Assert.Equal("b/n1", sorted[2].From);
        Assert.Equal("b", sorted[2].Sig);
        Assert.Equal("b/n2", sorted[3].From);
    }
}
