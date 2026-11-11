using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Time;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("week")]
public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
{
    public WeekSpend() : this(default, 0) { }
}

[Signal("test.charging")]
public sealed record Charging(decimal Amount) : Signal;

[State("nodefault")]
public sealed record NoDefault(int X);

public static class Registered
{
    [Synapse(typeof(Charging), "week")]
    public static WeekSpend React(Charging s, WeekSpend w) => w;

    [Synapse("week")]
    public static string View(WeekSpend w) => w.Spent.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

// Unattributed fixtures: the facts pass the attribute so the assembly-wide scan does not trip on the refused ones.
public sealed class Shapes
{
    public static WeekSpend React(Charging s, WeekSpend w) => w;

    public static Reaction<WeekSpend> ReactWithSignals(Charging s, WeekSpend w) => Reaction<WeekSpend>.Of(w);

    public static string View(WeekSpend w) => string.Empty;

    public static WeekSpend NoSignal(WeekSpend w) => w;

    public static WeekSpend TwoSignals(Charging a, Charging b, WeekSpend w) => w;

    public static WeekSpend WrongState(Charging s, string w) => new();

    public static string ViewWithSignal(Charging s, WeekSpend w) => string.Empty;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The fixture is deliberately an instance method.")]
    public WeekSpend Instance(Charging s, WeekSpend w) => w;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The fixture is the refused shape.")]
    public static WeekSpend TokenFirst(CancellationToken ct, Charging s, WeekSpend w) => w;

    public static WeekSpend TwoTokens(Charging s, WeekSpend w, CancellationToken a, CancellationToken b) => w;

    public static WeekSpend WithServices(Charging s, WeekSpend w, IServiceProvider sp) => w;

    public static Task<WeekSpend> WithSlot(Charging s, WeekSpend w, [Slot] ISqlRead db) => Task.FromResult(w);

    public static Task<WeekSpend> NamedSlot(Charging s, [Slot(Name = "Ledger")] ISqlRead db, WeekSpend w, CancellationToken ct) => Task.FromResult(w);

    public static WeekSpend NotAContract(Charging s, WeekSpend w, [Slot] string notAContract) => w;

    public static WeekSpend UnmarkedContract(Charging s, WeekSpend w, [Slot] IUnmarked db) => w;

    public static WeekSpend Daily(Tick t, WeekSpend w) => w;

    public static Reaction<WeekSpend> AsksOnAlias(SignalEnvelope s, WeekSpend w, [Slot] Asks<IProbe> feed) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> AnswerOnAlias(SignalEnvelope s, Answer one, WeekSpend w) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> AsksPoll(Charging s, WeekSpend w, [Slot] Asks<IProbe> feed) => Reaction<WeekSpend>.Of(w);

    public static Task<Reaction<WeekSpend>> AsksAsync(Charging s, WeekSpend w, [Slot] Asks<IProbe> feed) => Task.FromResult(Reaction<WeekSpend>.Of(w));

    public static Reaction<WeekSpend> AsksWithToken(Charging s, WeekSpend w, [Slot] Asks<IProbe> feed, CancellationToken ct) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> AsksAndCalling(Charging s, WeekSpend w, [Slot] Asks<IProbe> feed, [Slot] ISqlRead db) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> AsksOfPlainInterface(Charging s, WeekSpend w, [Slot] Asks<IDisposable> d) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> PerAnswer(Answered all, Answer one, WeekSpend w) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> AnswerOnCharging(Charging s, Answer one, WeekSpend w) => Reaction<WeekSpend>.Of(w);

    public static Reaction<WeekSpend> TwoAnswers(Answered all, Answer a, Answer b, WeekSpend w) => Reaction<WeekSpend>.Of(w);

    public static Task<Reaction<WeekSpend>> AnswerAsync(Answered all, Answer one, WeekSpend w) => Task.FromResult(Reaction<WeekSpend>.Of(w));
}

public sealed class SynapseShapeFacts
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

    private static readonly Dictionary<string, Type> States = new() { ["week"] = typeof(WeekSpend) };

    private static SynapseDescriptor Read(string name)
    {
        var attribute = name switch
        {
            nameof(Shapes.View) or nameof(Shapes.NoSignal) => new SynapseAttribute("week"),
            nameof(Shapes.AsksOnAlias) => new SynapseAttribute("x.go", "week"),
            nameof(Shapes.AnswerOnAlias) => new SynapseAttribute("unit.answered", "week"),
            nameof(Shapes.PerAnswer) or nameof(Shapes.TwoAnswers) or nameof(Shapes.AnswerAsync) => new SynapseAttribute(typeof(Answered), "week"),
            _ => new SynapseAttribute(typeof(Charging), "week"),
        };
        return SynapseShape.Of(typeof(Shapes).GetMethod(name, Flags)!, States, attribute);
    }

    [Theory]
    [InlineData(nameof(Shapes.React), SynapseKind.Reaction, false)]
    [InlineData(nameof(Shapes.ReactWithSignals), SynapseKind.Reaction, false)]
    [InlineData(nameof(Shapes.View), SynapseKind.View, false)]
    public void RecognisedShapes(string name, SynapseKind kind, bool ct)
    {
        var d = Read(name);
        Assert.Equal(kind, d.Kind);
        Assert.Equal(ct, d.TakesCancellation);
        Assert.Equal("week", d.StateAlias);
        Assert.Equal(typeof(WeekSpend), d.State);
        Assert.Equal(kind == SynapseKind.View ? null : typeof(Charging), d.Signal);
    }

    [Theory]
    [InlineData(nameof(Shapes.NoSignal), "gate needs a signal")]
    [InlineData(nameof(Shapes.TwoSignals), "at most one signal")]
    [InlineData(nameof(Shapes.WrongState), "state record 'week'")]
    [InlineData(nameof(Shapes.ViewWithSignal), "view takes only the state")]
    [InlineData(nameof(Shapes.Instance), "public static")]
    [InlineData(nameof(Shapes.TokenFirst), "not a signal, the state or CancellationToken")]
    [InlineData(nameof(Shapes.TwoTokens), "not a signal, the state or CancellationToken")]
    [InlineData(nameof(Shapes.WithServices), "not a signal, the state or CancellationToken")]
    public void RefusedShapes(string name, string fragment)
    {
        var e = Assert.Throws<SynapseShapeException>(() => Read(name));
        Assert.Contains(fragment, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlotParameterIsReadWithItsCapability()
    {
        var d = Read(nameof(Shapes.WithSlot));
        var slot = Assert.Single(d.Slots);
        Assert.Equal("db", slot.Name);
        Assert.Equal(typeof(ISqlRead), slot.Contract);
        Assert.Equal(new CapabilityId("sql-read", 1), slot.Capability);
        Assert.Equal(2, slot.ParameterIndex);
        Assert.Null(d.Every);
    }

    [Fact]
    public void ASlotNameComesFromTheAttributeAndMayPrecedeTheState()
    {
        var d = Read(nameof(Shapes.NamedSlot));
        Assert.Equal("Ledger", Assert.Single(d.Slots).Name);
        Assert.Equal(1, d.Slots[0].ParameterIndex);
        Assert.True(d.TakesCancellation);
    }

    [Theory]
    [InlineData(nameof(Shapes.NotAContract))]
    [InlineData(nameof(Shapes.UnmarkedContract))]
    public void ASlotWithoutACapabilityContractIsRefused(string name)
    {
        var e = Assert.Throws<SynapseShapeException>(() => Read(name));
        Assert.Contains("slot contract needs [Capability]", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOnATickReactionYieldsTheSchedule()
    {
        var d = SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.Daily))!, States, new SynapseAttribute(typeof(Tick), "week") { Every = "07:00" });
        Assert.Equal(ScheduleKind.Daily, d.Every!.Kind);
        Assert.Empty(d.Slots);
    }

    [Fact]
    public void ABadScheduleIsRefusedWithItsText()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.Daily))!, States, new SynapseAttribute(typeof(Tick), "week") { Every = "7pm" }));
        Assert.Contains("schedule: 7pm", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOnAReactionFromAnotherSignalIsRefused()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.React))!, States, new SynapseAttribute(typeof(Charging), "week") { Every = "07:00" }));
        Assert.Contains("every needs a Tick reaction", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOnAViewIsRefused()
    {
        var view = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.View))!, States, new SynapseAttribute("week") { Every = "07:00" }));
        Assert.Contains("every needs a Tick reaction", view.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReasonFragmentsAreTheSpecifiedText()
    {
        Assert.Equal("gate needs a signal", SynapseReasons.GateNeedsSignal);
        Assert.Equal("at most one signal", SynapseReasons.AtMostOneSignal);
        Assert.Equal("state record", SynapseReasons.StateRecord);
        Assert.Equal("view takes only the state", SynapseReasons.ViewTakesOnlyState);
        Assert.Equal("public static", SynapseReasons.PublicStatic);
        Assert.Equal("not a signal, the state or CancellationToken", SynapseReasons.UnknownParameter);
        Assert.Equal("declared signal type must be assignable to the signal parameter", SynapseReasons.SignalFitsParameter);
        Assert.Equal("slot contract needs [Capability]", SynapseReasons.SlotContractNeedsCapability);
        Assert.Equal("schedule", SynapseReasons.Schedule);
        Assert.Equal("every needs a Tick reaction", SynapseReasons.EveryNeedsTick);
        Assert.Equal("slot", SynapseReasons.SlotBoundTwice);
        Assert.Equal("alias reaction takes a SignalEnvelope, not a signal type", SynapseReasons.AliasNeedsEnvelope);
        Assert.Equal("Asks<> slot needs a synchronous reaction", SynapseReasons.AsksNeedSyncReaction);
        Assert.Equal("Asks<> slot cannot share a function with a calling slot", SynapseReasons.AsksExcludeCallingSlot);
        Assert.Equal("Answer parameter needs a synchronous reaction to Answered", SynapseReasons.AnswerNeedsAnswered);
    }

    [Fact]
    public void AnAsksSlotIsADeferredSlotOfItsContract()
    {
        var d = Read(nameof(Shapes.AsksPoll));
        var slot = Assert.Single(d.Slots);
        Assert.Equal(("feed", typeof(IProbe), new CapabilityId("probe", 1), true), (slot.Name, slot.Contract, slot.Capability, slot.Deferred));
        Assert.Equal(SynapseKind.Reaction, d.Kind);
        Assert.False(d.PerAnswer);
    }

    [Fact]
    public void AReactionToAnsweredMayTakeOneAnswer() => Assert.True(Read(nameof(Shapes.PerAnswer)).PerAnswer);

    [Fact]
    public void AnAliasReactionMayTakeAnAsksSlot()
    {
        var d = Read(nameof(Shapes.AsksOnAlias));
        Assert.Equal(("x.go", true, false), (d.SignalAlias, Assert.Single(d.Slots).Deferred, d.PerAnswer));
    }

    [Theory]
    [InlineData(nameof(Shapes.AnswerOnAlias), "Answer parameter needs a synchronous reaction to Answered")]
    [InlineData(nameof(Shapes.AsksAsync), "Asks<> slot needs a synchronous reaction")]
    [InlineData(nameof(Shapes.AsksWithToken), "Asks<> slot needs a synchronous reaction")]
    [InlineData(nameof(Shapes.AsksAndCalling), "Asks<> slot cannot share a function with a calling slot")]
    [InlineData(nameof(Shapes.AsksOfPlainInterface), "slot contract needs [Capability]")]
    [InlineData(nameof(Shapes.AnswerOnCharging), "Answer parameter needs a synchronous reaction to Answered")]
    [InlineData(nameof(Shapes.TwoAnswers), "Answer parameter needs a synchronous reaction to Answered")]
    [InlineData(nameof(Shapes.AnswerAsync), "Answer parameter needs a synchronous reaction to Answered")]
    public void TheAskShapesThatAreRefused(string name, string fragment) =>
        Assert.Contains(fragment, Assert.Throws<SynapseShapeException>(() => Read(name)).Message, StringComparison.Ordinal);

    [Fact]
    public void ADeclaredSignalThatDoesNotFitTheParameterIsRefused()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.React))!, States, new SynapseAttribute(typeof(Signal), "week")));
        Assert.Contains(SynapseReasons.SignalFitsParameter, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyKeyIsRefused() =>
        Assert.Equal("into", Assert.Throws<ArgumentException>(() => new SynapseAttribute(typeof(Charging), "week/")).ParamName);

    [Fact]
    public void AFromTypeThatIsNotASignalIsRefused() =>
        Assert.Equal("from", Assert.Throws<ArgumentException>(() => new SynapseAttribute(typeof(string), "week")).ParamName);

    [Fact]
    public void AnAliasOutsideTheStatesIsRefusedAsStateRecord()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.React))!, new Dictionary<string, Type>(), new SynapseAttribute(typeof(Charging), "week")));
        Assert.Contains("state record 'week'", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyComesFromTheAttribute() =>
        Assert.Equal("2026-45", SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.React))!, States, new SynapseAttribute(typeof(Charging), "week/2026-45")).Key);

    [Fact]
    public void PublicOfRequiresTheAttribute() =>
        Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(Shapes).GetMethod(nameof(Shapes.React))!, States));

    [Fact]
    public void PublicOfReadsTheMethodAttribute() =>
        Assert.Equal(SynapseKind.Reaction, SynapseShape.Of(typeof(Registered).GetMethod(nameof(Registered.React))!, States).Kind);
}
