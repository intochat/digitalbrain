using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace IntoChat.Apps;

// The paid Image Editor operation's provider seam. The product wires a deterministic fake; a hosted
// deployment wires the third-party adapter behind the same contract.
public interface IBackgroundRemover
{
    Task<BackgroundRemovalOutput> RemoveAsync(BackgroundRemovalInput input, CancellationToken cancellationToken = default);
}

public sealed record BackgroundRemovalInput(string ImageId, string AssetId, string Name);

public sealed record BackgroundRemovalOutput(bool Succeeded, string? NewAssetId, decimal ChargedCompute, string? FailureReason);

public sealed class BackgroundRemovalOptions
{
    public const string SectionName = "IntoChat:BackgroundRemoval";
    public decimal EstimatedComputePerImage { get; set; } = 4m;
    public decimal ActualComputePerImage { get; set; } = 4.5m;
    public decimal MaximumCompute { get; set; } = 20m;
    public string[] FailingImages { get; set; } = [];
}

// The same image always yields the same result, and the configured images fail as a third-party
// fault. It never calls a real paid service.
internal sealed class DeterministicBackgroundRemover(IOptions<BackgroundRemovalOptions> options) : IBackgroundRemover
{
    public Task<BackgroundRemovalOutput> RemoveAsync(BackgroundRemovalInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        var settings = options.Value;
        if (settings.FailingImages.Contains(input.Name, StringComparer.Ordinal))
        {
            return Task.FromResult(new BackgroundRemovalOutput(false, null, 0m, "The provider could not process this image (third-party fault)."));
        }
        return Task.FromResult(new BackgroundRemovalOutput(true, input.AssetId + "-transparent", settings.ActualComputePerImage, null));
    }
}

[GenerateSerializer, Alias("intochat.background-removal-image")]
public sealed record BackgroundRemovalImage
{
    [Id(0)] public required string ImageId { get; init; }
    [Id(1)] public required string AssetId { get; init; }
    [Id(2)] public required string Name { get; init; }
}

[GenerateSerializer, Alias("intochat.background-removal-option")]
public sealed record BackgroundRemovalAllowanceOption
{
    [Id(0)] public required string Label { get; init; }
    [Id(1)] public required AllowanceScope Scope { get; init; }
    [Id(2)] public required decimal LimitCompute { get; init; }
}

// The plan card: the app sees only the selected images, with an estimate and a maximum. An absent
// allowance arrives as a pending ApprovalId the owner answers with "Allow once" or "Always".
[GenerateSerializer, Alias("intochat.background-removal-plan")]
public sealed record BackgroundRemovalPlan
{
    [Id(0)] public required string PlanId { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required IReadOnlyList<BackgroundRemovalImage> Images { get; init; }
    [Id(3)] public required decimal EstimatedCompute { get; init; }
    [Id(4)] public required decimal MaximumCompute { get; init; }
    [Id(5)] public string? ApprovalId { get; init; }
    [Id(6)] public required IReadOnlyList<BackgroundRemovalAllowanceOption> Options { get; init; }
}

[GenerateSerializer, Alias("intochat.background-removal-image-result")]
public sealed record BackgroundRemovalImageResult
{
    [Id(0)] public required string ImageId { get; init; }
    [Id(1)] public required bool Succeeded { get; init; }
    [Id(2)] public required decimal ChargedCompute { get; init; }
    [Id(3)] public string? VersionId { get; init; }
    [Id(4)] public string? FailureReason { get; init; }
}

// The receipt: actual charged, with each failed image not charged.
[GenerateSerializer, Alias("intochat.background-removal-receipt")]
public sealed record BackgroundRemovalReceipt
{
    [Id(0)] public required string PlanId { get; init; }
    [Id(1)] public required string IntentId { get; init; }
    [Id(2)] public required IReadOnlyList<BackgroundRemovalImageResult> Images { get; init; }
    [Id(3)] public required decimal ChargedCompute { get; init; }
    [Id(4)] public required decimal EstimatedCompute { get; init; }
    [Id(5)] public required decimal MaximumCompute { get; init; }
    [Id(6)] public required bool RequiresApproval { get; init; }
    [Id(7)] public string? ReservationId { get; init; }
}

[Alias("intochat.background-removal"), Orleans.Metadata.DefaultGrainType("intochat.background-removal")]
public interface IBackgroundRemoval : INeuron
{
    Task<BackgroundRemovalPlan> Plan(IReadOnlyList<string> imageIds, string intentId);
    Task<BackgroundRemovalReceipt> Run(string planId, string intentId);
    [Orleans.Concurrency.ReadOnly] Task<BackgroundRemovalPlan[]> ReadPlans();
}

[GenerateSerializer, Alias("intochat.background-removal-state")]
internal sealed record BackgroundRemovalState
{
    [Id(0)] public List<BackgroundRemovalPlan> Plans { get; init; } = [];
}

[GrainType("intochat.background-removal")]
internal sealed class BackgroundRemovalNeuron(IGrainFactory grains, IBackgroundRemover remover, ICallFilter filter, IOptions<BackgroundRemovalOptions> options,
    [PersistentState("background-removal", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BackgroundRemovalState> store) : Neuron, IBackgroundRemoval
{
    private const string AppId = "intochat.image-editor";
    private const string Operation = "RemoveBackground";
    private static string AccountId => AccountSession.DefaultLogin;
    private string Scope => this.GetPrimaryKeyString();
    private string Workspace => Scope[..Scope.IndexOf("/apps/", StringComparison.Ordinal)];

    public async Task<BackgroundRemovalPlan> Plan(IReadOnlyList<string> imageIds, string intentId)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        if (imageIds.Count is 0 or > 64) { throw new ArgumentException("Select 1–64 images.", nameof(imageIds)); }
        var images = new List<BackgroundRemovalImage>(imageIds.Count);
        foreach (var imageId in imageIds)
        {
            var document = await grains.GetGrain<IImageDocument>(Workspace + "/images/" + imageId).Read();
            if (document.Asset is null) { throw new KeyNotFoundException($"Image '{imageId}' is not open in this workspace."); }
            images.Add(new BackgroundRemovalImage { ImageId = document.Id, AssetId = document.Asset.Id, Name = document.Asset.Name });
        }

        var estimated = options.Value.EstimatedComputePerImage * images.Count;
        var maximum = options.Value.MaximumCompute;
        var decision = await Ledger().AuthorizeAsync(Request(estimated, intentId), CancellationToken.None);
        var plan = new BackgroundRemovalPlan
        {
            PlanId = Guid.NewGuid().ToString("n"),
            AccountId = AccountId,
            Images = images,
            EstimatedCompute = estimated,
            MaximumCompute = maximum,
            ApprovalId = decision.Allowed ? null : decision.ApprovalId,
            Options =
            [
                new BackgroundRemovalAllowanceOption { Label = "Allow once", Scope = AllowanceScope.Once, LimitCompute = maximum },
                new BackgroundRemovalAllowanceOption { Label = "Always, up to 100 a month", Scope = AllowanceScope.Always, LimitCompute = 100m },
            ],
        };

        var previous = store.State;
        store.State = store.State with { Plans = [.. store.State.Plans, plan] };
        try { await store.WriteStateAsync(); } catch { store.State = previous; throw; }
        return plan;
    }

    public async Task<BackgroundRemovalReceipt> Run(string planId, string intentId)
    {
        var plan = store.State.Plans.LastOrDefault(item => string.Equals(item.PlanId, planId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("The plan is no longer available; plan again.");
        // Every paid app operation goes through the one call filter; the allowance stage decides. The
        // filter creates the deterministic reservation, which Run settles once the batch finishes.
        var request = Request(plan.EstimatedCompute, intentId);
        CallerContextStamper.Stamp(request.Caller);
        var decision = await filter.AuthorizeAsync(request, CancellationToken.None);
        if (!decision.Allowed)
        {
            return new BackgroundRemovalReceipt
            {
                PlanId = planId,
                IntentId = intentId,
                Images = [],
                ChargedCompute = 0m,
                EstimatedCompute = plan.EstimatedCompute,
                MaximumCompute = plan.MaximumCompute,
                RequiresApproval = decision.Denial == CallDenial.MissingAllowance,
                ReservationId = null,
            };
        }

        var results = new List<BackgroundRemovalImageResult>(plan.Images.Count);
        var charged = 0m;
        var anySucceeded = false;
        foreach (var image in plan.Images)
        {
            var output = await remover.RemoveAsync(new(image.ImageId, image.AssetId, image.Name), CancellationToken.None);
            if (!output.Succeeded)
            {
                results.Add(new BackgroundRemovalImageResult { ImageId = image.ImageId, Succeeded = false, ChargedCompute = 0m, FailureReason = output.FailureReason });
                continue;
            }
            var version = await grains.GetGrain<IImageDocument>(Workspace + "/images/" + image.ImageId).AddVersion("background-removed", output.NewAssetId!);
            charged += output.ChargedCompute;
            anySucceeded = true;
            results.Add(new BackgroundRemovalImageResult { ImageId = image.ImageId, Succeeded = true, ChargedCompute = output.ChargedCompute, VersionId = version.Versions[^1].VersionId });
        }

        var failure = anySucceeded ? FailureClass.None : FailureClass.ThirdPartyFault;
        var reservationId = AllowanceReservations.For(request);
        await Ledger().SettleAsync(reservationId, charged, failure, CancellationToken.None);
        return new BackgroundRemovalReceipt
        {
            PlanId = planId,
            IntentId = intentId,
            Images = results,
            ChargedCompute = charged,
            EstimatedCompute = plan.EstimatedCompute,
            MaximumCompute = plan.MaximumCompute,
            RequiresApproval = false,
            ReservationId = reservationId,
        };
    }

    public Task<BackgroundRemovalPlan[]> ReadPlans() => Task.FromResult<BackgroundRemovalPlan[]>([.. store.State.Plans]);

    private IAllowanceLedger Ledger() => grains.GetGrain<IAllowanceLedger>(AccountId);

    private CallRequest Request(decimal estimated, string intentId) => new()
    {
        Caller = new CallerContext
        {
            PrincipalId = AccountId,
            AccountId = AccountId,
            WorkspaceId = Workspace,
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
            AppId = AppId,
            IntentId = intentId,
        },
        TargetNeuron = Scope + "/apps/image-editor",
        Operation = Operation,
        EstimatedCompute = estimated,
        HasSideEffects = true,
    };
}
