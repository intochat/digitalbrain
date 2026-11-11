
using DigitalBrain.Neurons;

namespace DigitalBrain.Framework.Tests.E2E.WeeklyCap;

[Signal("payments.charging")]
public sealed record Charging(decimal Amount) : Signal;

[Signal("payments.charging-risky")]
public sealed record ChargingRisky(decimal Amount) : Signal;

[Signal("weeklycap.week-rolled")]
public sealed record WeekRolled(DateOnly Week) : Signal;

public sealed record PayState
{
    public int Count { get; init; }
}

public interface IPayments
{
    Task Charge(decimal amount);

    Task ChargeRisky(decimal amount);

    Task Roll(DateOnly week);
}

[Neuron("payments")]
public sealed class Payments : Neuron<PayState>, IPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new Charging(amount));

    public Task ChargeRisky(decimal amount) => Save(State with { Count = State.Count + 1 }, new ChargingRisky(amount));

    public Task Roll(DateOnly week) => Save(State with { Count = State.Count + 1 }, new WeekRolled(week));
}
