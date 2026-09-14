using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal static class SlotEndpoints
{
    // How one slot asks the other whether a lease flip has reached it, and how an operator reads a slot
    // without the dashboard. A silo answers from its own lease, the only slot fact it can know, and names
    // the slot it is: a standby whose refresher has not seen the flip yet answers false, a host that was
    // given no slot name answers false for every name, and the answer needs no slot neuron, so a host that
    // composed no Coding module still serves the route.
    public static IEndpointRouteBuilder MapSlotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/slots/{slot}",
            static IResult (string slot, IActiveSlotLease lease) => Results.Ok(
                new SlotLeaseView(lease.Slot, lease.HoldsLease && AnswersFor(lease.Slot, slot), lease.Generation)))
            .AddEndpointFilter(new NeuronNameFilter("slot"));

        return endpoints;
    }

    private static bool AnswersFor(string leaseSlot, string slot)
        => leaseSlot.Length > 0 && string.Equals(leaseSlot, slot, StringComparison.OrdinalIgnoreCase);
}

// LeaseGeneration counts lease handovers, and is deliberately not the slot's git Generation: the two would
// otherwise be one word apart in a promotion's logs.
internal sealed record SlotLeaseView(string Slot, bool HoldsLease, long LeaseGeneration);
