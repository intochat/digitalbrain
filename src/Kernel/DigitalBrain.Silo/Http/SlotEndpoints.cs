using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal static class SlotEndpoints
{
    // How one slot asks the other whether a lease flip has reached it, and how an operator reads a slot
    // without the dashboard. A silo answers from its own lease, the only slot fact it can know: a standby
    // whose refresher has not seen the flip yet answers false, and the answer needs no slot neuron, so a
    // host that composed no Coding module still serves the route.
    public static IEndpointRouteBuilder MapSlotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/slots/{slot}",
            static IResult (string slot, IActiveSlotLease lease) => Results.Ok(
                new SlotLeaseView(slot, lease.HoldsLease && AnswersFor(lease.Slot, slot), lease.Generation)))
            .AddEndpointFilter(new NeuronNameFilter("slot"));

        return endpoints;
    }

    // A single-slot host carries no slot name and is whichever slot it is asked about; a slotted one
    // answers for its own name only, so a mis-paired promotion never reads a flip that did not happen.
    private static bool AnswersFor(string leaseSlot, string slot)
        => leaseSlot.Length == 0 || string.Equals(leaseSlot, slot, StringComparison.OrdinalIgnoreCase);
}

internal sealed record SlotLeaseView(string Slot, bool HoldsLease, long Generation);
