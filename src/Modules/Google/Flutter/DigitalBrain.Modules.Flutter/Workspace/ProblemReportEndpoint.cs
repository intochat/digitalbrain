using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Flutter.Workspace;

internal static class ProblemReportEndpoint
{
    public static async Task<IResult> File(ProblemReportInput input, IDigitalBrain brain, CancellationToken ct)
    {
        if (!BrainScope.IsValidId(input.IntentId) || string.IsNullOrWhiteSpace(input.Message))
        { return Results.BadRequest(new { error = "A report needs the intent id it came from and a message." }); }
        try
        {
            var caller = CallerContextStamper.Require();
            var scope = BrainScope.Create(caller.AccountId, caller.BrainId);
            var report = await brain.Get<IProblemReports>(scope.Id).Add(scope.Name, input.IntentId!, input.Message!.Trim()).WaitAsync(ct);
            return Results.Created($"/brains/{scope.Name}/reports", report);
        }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    }
}
