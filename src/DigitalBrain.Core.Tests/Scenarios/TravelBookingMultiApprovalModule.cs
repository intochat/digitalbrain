using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record TravelSearchAsked(
    string TripId,
    string Destination,
    string Window) : Synapse;

public sealed record TravelOffer(string OfferId, string Kind, double Price, bool InPolicy);

public sealed record TravelOffersPresented(
    string TripId,
    ImmutableArray<TravelOffer> Offers) : Synapse;

public sealed record TravelPolicyEvaluated(
    string TripId,
    string OfferId,
    bool InPolicy,
    ImmutableArray<string> Reasons) : Synapse;

public sealed record TravelSelectionMade(
    string TripId,
    string OfferId) : Synapse;

public sealed record TravelApprovalRequired(
    string TripId,
    string OfferId,
    string BundleId) : Synapse;

public sealed record TravelManagerApproved(
    string BundleId,
    string TripId,
    string OfferId) : Synapse;

public sealed record TravelHoldPlaced(
    string TripId,
    string OfferId,
    string HoldId) : Synapse;

public sealed record TravelBookConfirmed(string TripId) : Synapse;

public sealed record TravelBooked(
    string TripId,
    string OfferId,
    string Pnr) : Synapse;

public sealed record TravelCalendarHoldCreated(
    string TripId,
    string Title) : Synapse;

// Travel saga: offers + policy → selection → deferred manager approval → hold → book → calendar.
public sealed class TravelBookingDesk : Neuron<TravelBookingState>,
    INeuron<TravelSearchAsked>,
    INeuron<TravelSelectionMade>,
    INeuron<TravelManagerApproved>,
    INeuron<TravelBookConfirmed>
{
    public Task HandleAsync(TravelSearchAsked fact, CancellationToken cancellationToken)
    {
        State.TripId = fact.TripId;
        State.Phase = "offers";
        var offers = ImmutableArray.Create(
            new TravelOffer("air-in", "flight", 320, InPolicy: true),
            new TravelOffer("hotel-out", "hotel", 480, InPolicy: false));
        Emit(new TravelOffersPresented(fact.TripId, offers));
        foreach (var offer in offers)
        {
            Emit(new TravelPolicyEvaluated(
                fact.TripId,
                offer.OfferId,
                offer.InPolicy,
                Reasons: offer.InPolicy
                    ? ["within-cap"]
                    : ["hotel-over-nightly-cap"]));
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(TravelSelectionMade fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.TripId, fact.TripId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.SelectedOfferId = fact.OfferId;
        State.Phase = "awaiting-approval";
        var bundleId = $"travel-{fact.TripId}-{fact.OfferId}";
        State.BundleId = bundleId;
        Emit(new TravelApprovalRequired(fact.TripId, fact.OfferId, bundleId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(TravelManagerApproved fact, CancellationToken cancellationToken)
    {
        if (State.BundleId is null
            || !string.Equals(fact.BundleId, State.BundleId, StringComparison.Ordinal)
            || !string.Equals(fact.TripId, State.TripId, StringComparison.Ordinal)
            || State.Booked)
        {
            return Task.CompletedTask;
        }

        State.Phase = "held";
        State.HoldId = $"hold-{fact.OfferId}";
        Emit(new TravelHoldPlaced(fact.TripId, fact.OfferId, State.HoldId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(TravelBookConfirmed fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.TripId, fact.TripId, StringComparison.Ordinal)
            || State.HoldId is null
            || State.SelectedOfferId is null
            || State.Booked)
        {
            return Task.CompletedTask;
        }

        State.Booked = true;
        State.Phase = "booked";
        Emit(new TravelBooked(fact.TripId, State.SelectedOfferId, Pnr: $"PNR-{fact.TripId}"));
        Emit(new TravelCalendarHoldCreated(fact.TripId, Title: $"Travel {fact.TripId}"));
        return Task.CompletedTask;
    }
}

public sealed class TravelBookingState
{
    public string? TripId { get; set; }
    public string? SelectedOfferId { get; set; }
    public string? BundleId { get; set; }
    public string? HoldId { get; set; }
    public string? Phase { get; set; }
    public bool Booked { get; set; }
}

// Catalog sinks for travel ambient saga facts.
public sealed class TravelSagaLedger : Neuron,
    INeuron<TravelOffersPresented>,
    INeuron<TravelPolicyEvaluated>,
    INeuron<TravelApprovalRequired>,
    INeuron<TravelHoldPlaced>,
    INeuron<TravelBooked>,
    INeuron<TravelCalendarHoldCreated>
{
    public Task HandleAsync(TravelOffersPresented fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelPolicyEvaluated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelApprovalRequired fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelHoldPlaced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelBooked fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelCalendarHoldCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
