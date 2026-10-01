using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DigitalBrain.Core.Enforcement;
using Orleans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter.Workspace;

internal static class ShellPersistenceEndpoints
{
    internal static string Scope(string account, string principal) => "shell-" + ShellSnapshotParts.Digest(account + "\0" + principal);
    public static void MapShellPersistence(this IEndpointRouteBuilder routes)
    {
        static IShellState State(IGrainFactory grains)
        {
            var caller = CallerContextStamper.Require();
            return grains.GetGrain<IShellState>(Scope(caller.AccountId, caller.PrincipalId));
        }
        static IResult View(ShellRead value) => Results.Ok(new { value.Revision, Snapshot = value.Json is null ? null : JsonNode.Parse(value.Json) });
        routes.MapGet("/shell/state", async (IGrainFactory grains) => View(await State(grains).Read()));
        static async Task<IResult> Save(ShellWrite input, IGrainFactory grains)
        {
            try
            {
                var json = input.Snapshot.ToJsonString();
                if (Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024) { return Results.StatusCode(413); }
                return View(await State(grains).Save(input.ExpectedRevision, input.OperationId, json));
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
        }
        routes.MapPut("/shell/state", (ShellWrite input, IGrainFactory grains) => Save(input, grains));
    }
    internal sealed record ShellWrite(long ExpectedRevision, string OperationId, JsonNode Snapshot);
}
