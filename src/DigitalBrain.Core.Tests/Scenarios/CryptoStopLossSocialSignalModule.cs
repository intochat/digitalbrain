using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

// Price stream tick — joins with armed social stop in journaled RiskPolicy state.
public sealed record PriceTick(string Asset, double Price) : Synapse;

public sealed record StopLossArmed(
    string Asset,
    double StopPrice,
    double Fraction,
    string PostId) : Synapse;

public sealed record StopLossTriggered(
    string Asset,
    double Price,
    double Fraction,
    string Reason,
    string PostId) : Synapse;

public sealed record OrderFilled(
    string Asset,
    double Fraction,
    double FillPrice,
    string PostId) : Synapse;

public sealed class RiskPolicyState
{
    public string? ArmedAsset { get; set; }
    public double StopPrice { get; set; }
    public double Fraction { get; set; }
    public string? ArmedPostId { get; set; }
    public bool Triggered { get; set; }
}

// Joins XPostObserved panic language with PriceTick against a journaled arm — no silent fields alone.
public sealed class RiskPolicy : Neuron<RiskPolicyState>,
    INeuron<XPostObserved>,
    INeuron<PriceTick>
{
    public const string TrackedAsset = "BTC";
    public const double DefaultStop = 90_000.0;
    public const double DefaultFraction = 0.25;

    public Task HandleAsync(XPostObserved fact, CancellationToken cancellationToken)
    {
        if (State.ArmedAsset is not null || State.Triggered)
        {
            return Task.CompletedTask;
        }

        if (!MentionsAsset(fact.Text) || !IsPanicLanguage(fact.Text))
        {
            return Task.CompletedTask;
        }

        State.ArmedAsset = TrackedAsset;
        State.StopPrice = DefaultStop;
        State.Fraction = DefaultFraction;
        State.ArmedPostId = fact.PostId;
        Emit(new StopLossArmed(TrackedAsset, DefaultStop, DefaultFraction, fact.PostId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PriceTick fact, CancellationToken cancellationToken)
    {
        if (State.Triggered
            || State.ArmedAsset is null
            || State.ArmedPostId is null
            || !string.Equals(fact.Asset, State.ArmedAsset, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (fact.Price > State.StopPrice)
        {
            return Task.CompletedTask;
        }

        // Journal gate: one trigger per arm — at-least-once ticks must not double-sell.
        State.Triggered = true;
        Emit(new StopLossTriggered(
            State.ArmedAsset,
            fact.Price,
            State.Fraction,
            Reason: $"price {fact.Price} crossed stop {State.StopPrice} after social {State.ArmedPostId}",
            State.ArmedPostId));
        return Task.CompletedTask;
    }

    private static bool MentionsAsset(string text)
        => text.Contains(TrackedAsset, StringComparison.OrdinalIgnoreCase);

    private static bool IsPanicLanguage(string text)
        => text.Contains("panic", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dump", StringComparison.OrdinalIgnoreCase)
            || text.Contains("crash", StringComparison.OrdinalIgnoreCase);
}

// Mock exchange: irreversible fill journals from StopLossTriggered only.
public sealed class PortfolioBroker : Neuron, INeuron<StopLossTriggered>
{
    public Task HandleAsync(StopLossTriggered fact, CancellationToken cancellationToken)
    {
        Emit(new OrderFilled(fact.Asset, fact.Fraction, fact.Price, fact.PostId));
        return Task.CompletedTask;
    }
}

// Catalog sinks for ambient arm / trigger / fill facts.
public sealed class StopLossDeskLedger : Neuron,
    INeuron<StopLossArmed>,
    INeuron<StopLossTriggered>,
    INeuron<OrderFilled>
{
    public Task HandleAsync(StopLossArmed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(StopLossTriggered fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(OrderFilled fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
