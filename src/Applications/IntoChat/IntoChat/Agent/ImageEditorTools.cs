using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Collection;
using IntoChat.Apps;
using IntoChat.LocalFiles;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// The paid Image Editor operation: the model plans over the workspace's images and shows the plan
// card, then, after the owner allows it, approves the allowance and runs the batch.
internal sealed class ImageEditorTools(IDigitalBrain brain, LocalFileStore files) : IAgentToolFactory
{
    internal const string ImageEditorAppId = "intochat.image-editor";

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> PlanBackgroundRemoval(
            [Description("How many of the workspace's images to include, in file order.")] int imageCount,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await PlanAsync(trusted.ScopeId, trusted.RunId, imageCount, ct); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        async Task<object> RunBackgroundRemoval(
            [Description("The planId returned by plan_background_removal; omit to use the latest plan.")] string? planId,
            [Description("The owner's approval: 'once' or 'always'; omit to run without an allowance and prove nothing is charged.")] string? allowance,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await RunAsync(trusted.ScopeId, trusted.RunId, planId, allowance, ct); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        return
        [
            AIFunctionFactory.Create(PlanBackgroundRemoval, "plan_background_removal", "Plan background removal over the workspace's images. Returns a plan card with the estimate, the maximum and an approval id; nothing is charged yet."),
            AIFunctionFactory.Create(RunBackgroundRemoval, "run_background_removal", "Run an approved background-removal plan. Without an allowance it returns requiresApproval and charges nothing."),
        ];
    }

    private async Task<object> PlanAsync(string scope, string intentId, int imageCount, CancellationToken ct)
    {
        if (imageCount is < 1 or > 64) { throw new ArgumentException("Choose 1–64 images.", nameof(imageCount)); }
        var page = await files.ListAsync(scope, null, 0, "name", "", ct);
        var images = page.Items.Where(IsImage).Take(imageCount).ToArray();
        if (images.Length == 0) { throw new KeyNotFoundException("No images are in the workspace Files root."); }
        var explorer = brain.Get<IFileExplorer>(scope);
        var imageIds = new List<string>(images.Length);
        foreach (var entry in images) { imageIds.Add((await explorer.OpenImage(entry.Id).WaitAsync(ct)).Id); }

        var plan = await Removal(scope).Plan(imageIds, intentId).WaitAsync(ct);
        return new
        {
            planId = plan.PlanId,
            images = plan.Images.Count,
            estimatedCompute = plan.EstimatedCompute,
            maximumCompute = plan.MaximumCompute,
            requiresApproval = plan.ApprovalId is not null,
            _ui = new
            {
                kind = "plan-card",
                planId = plan.PlanId,
                images = plan.Images.Select(image => new { imageId = image.ImageId, name = image.Name }).ToArray(),
                estimate = plan.EstimatedCompute,
                maximum = plan.MaximumCompute,
                options = plan.Options.Select(option => new { label = option.Label, scope = option.Scope.ToString(), limit = option.LimitCompute }).ToArray(),
            },
        };
    }

    private async Task<object> RunAsync(string scope, string intentId, string? planId, string? allowance, CancellationToken ct)
    {
        var plans = await Removal(scope).ReadPlans().WaitAsync(ct);
        var plan = string.IsNullOrWhiteSpace(planId)
            ? plans.LastOrDefault()
            : plans.LastOrDefault(candidate => candidate.PlanId == planId);
        if (plan is null) { throw new KeyNotFoundException("No plan is available; plan again."); }
        planId = plan.PlanId;
        if (plan.ApprovalId is not null && !string.IsNullOrWhiteSpace(allowance))
        {
            var always = allowance.Equals("always", StringComparison.OrdinalIgnoreCase);
            await brain.Get<IAllowanceLedger>(plan.AccountId).ApproveAsync(plan.ApprovalId, always ? ApprovalLevel.InstallConsent : ApprovalLevel.PerOperation,
                always ? AllowanceScope.Always : AllowanceScope.Once, always ? 100m : plan.MaximumCompute, ct);
        }

        var receipt = await Removal(scope).Run(planId, intentId).WaitAsync(ct);
        return new
        {
            planId,
            requiresApproval = receipt.RequiresApproval,
            chargedCompute = receipt.ChargedCompute,
            succeeded = receipt.Images.Count(image => image.Succeeded),
            failed = receipt.Images.Count(image => !image.Succeeded),
            _ui = new
            {
                kind = "charge-receipt",
                planId,
                chargedCompute = receipt.ChargedCompute,
                maximum = receipt.MaximumCompute,
                requiresApproval = receipt.RequiresApproval,
                images = receipt.Images.Select(image => new { imageId = image.ImageId, succeeded = image.Succeeded, charged = image.ChargedCompute }).ToArray(),
            },
        };
    }

    private IBackgroundRemoval Removal(string scope) => brain.Get<IBackgroundRemoval>(scope + "/apps/image-editor/background-removal");

    private static bool IsImage(CollectionItem item) =>
        item.Kind.Contains("image", StringComparison.OrdinalIgnoreCase)
        || item.Label.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || item.Label.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || item.Label.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || item.Label.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
        || item.Label.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}