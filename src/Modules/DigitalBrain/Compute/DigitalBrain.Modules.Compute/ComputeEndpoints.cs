using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Compute;

internal static class ComputeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/compute/summary", async (IDigitalBrain brain, CancellationToken ct) =>
        {
            var accountId = CallerContextStamper.Require().AccountId;
            var ledger = brain.Get<IAllowanceLedger>(accountId);
            var limits = await ledger.ReadLimitsAsync(ct);
            return Results.Ok(new
            {
                chargedCompute = await brain.Get<IWallet>(accountId).ReadChargedAsync(cancellationToken: ct),
                settledCompute = await ledger.ReadSettledAsync(cancellationToken: ct),
                reservedCompute = await ledger.ReadReservedAsync(cancellationToken: ct),
                limits.LimitCompute,
                limits.SpentCompute,
                limits.HardStopped,
            });
        });
        endpoints.MapGet("/compute/limits", async (IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await brain.Get<IAllowanceLedger>(CallerContextStamper.Require().AccountId).ReadLimitsAsync(ct)));
    }
}
