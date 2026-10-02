using DigitalBrain.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Apps;

internal sealed record DraftRequest(string Text);
internal sealed record ConvertDraftRequest(long ExpectedRevision);

internal static class MarketplaceEndpoints
{
    public static void MapMarketplace(this IEndpointRouteBuilder routes)
    {
        var packages = routes.MapGroup("/packages").AddEndpointFilter(ModuleRouteGuard.Guard);
        if (routes.ServiceProvider.GetService<IScriptSandbox>()?.CanRun == true)
        { PackageEndpoints.MapAuthoring(packages); }
        packages.MapGet("/drafts", (IDigitalBrain brain)
            => brain.Get<IAppDrafts>(CallerContextStamper.Require().PrincipalId).List());
        packages.MapPost("/drafts/{id}", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).Draft(request.Text));
        packages.MapGet("/drafts/{id}", (string id, IDigitalBrain brain) => Draft(brain, id).Read());
        packages.MapPost("/drafts/{id}/revise", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).Revise(request.Text));
        packages.MapPut("/drafts/{id}/spec", (string id, DraftRequest request, IDigitalBrain brain)
            => Draft(brain, id).EditSpec(request.Text));
        packages.MapPut("/drafts/{id}/document", (string id, SaveAppDocument request, IDigitalBrain brain)
            => DocumentResult(() => Draft(brain, id).SaveDocument(request)));
        packages.MapPost("/drafts/{id}/import", (string id, ImportAppDocument request, IDigitalBrain brain)
            => DocumentResult(() => Draft(brain, id).ImportRevision(request.Revision, request.ExpectedRevision)));
        packages.MapPost("/drafts/{id}/conversion", async (string id, ConvertDraftRequest request, IDigitalBrain brain) =>
        {
            try { return Results.Ok(await Draft(brain, id).ProposeConversion(request.ExpectedRevision)); }
            catch (AppDraftConflictException e) { return Results.Problem(e.Message, statusCode: 409); }
        });
        packages.MapPost("/drafts/{id}/build", (string id, IDigitalBrain brain) => Draft(brain, id).Build());
        packages.MapGet("/{owner}/{name}/spec", (string owner, string name, string? revision, MarketplaceService marketplace)
            => marketplace.Spec(PackageId.Create(owner, name), revision));
        packages.MapPost("/{owner}/{name}/verify", (string owner, string name, string? revision, MarketplaceService marketplace)
            => marketplace.Verify(PackageId.Create(owner, name), revision));

        var installed = BrainRoutes.Group(routes, "/packages/{owner}/{name}").AddEndpointFilter(ModuleRouteGuard.Guard);
        installed.MapGet("/invocations/{invocationId:guid}/discussion", (string owner, string name, Guid invocationId, MarketplaceService marketplace)
            => marketplace.Discussion(InstalledPackages.AppKey(PackageId.Create(owner, name)), invocationId));
    }

    // A draft belongs to whoever is signed in, and the app it builds is published under their name.
    private static IAppDraft Draft(IDigitalBrain brain, string id)
    {
        if (!Guid.TryParse(id, out var draftId)) { throw new ArgumentException("A draft id is a GUID."); }
        return brain.Get<IAppDraft>($"{CallerContextStamper.Require().PrincipalId}/drafts/{draftId:N}");
    }

    private static async Task<IResult> DocumentResult(Func<Task<AppDraftView>> action)
    {
        try { return Results.Ok(await action()); }
        catch (AppDraftConflictException e) { return Results.Problem(e.Message, statusCode: 409); }
    }
}




