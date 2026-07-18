using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record AgentRequest(string Prompt, string? PreferredModel = null) : Synapse;

[GenerateSerializer]
public sealed record AgentResponse(string Prompt, string Content) : Synapse;

[GenerateSerializer]
public sealed record AgentAwareness(NeuronId Agent, string Aspect, string Detail) : Synapse;

// Phase 07 real-world + self-dev: file ops and self-improvement as first-class synapses (neurons react, emit results/proposals).
// Net phase: use preview union for closed set of file ops (expressive, exhaustive in neuron, DDD). Follows exact KernelTaskStatus pattern (separate case records + union).
[GenerateSerializer]
public sealed record FileSave(string Path, string Content, string? Description = null);

[GenerateSerializer]
public sealed record FileRead(string Path, string? Description = null);

[GenerateSerializer]
public union FileOp(FileSave, FileRead);

[GenerateSerializer]
public sealed record SaveFileRequest(FileOp Op) : Synapse;

[GenerateSerializer]
public sealed record FileSaved(
    [property: Id(0)] string Path,
    [property: Id(1)] long BytesWritten,
    [property: Id(2)] string? Message = null) : Synapse;

[GenerateSerializer]
public sealed record FileReadResult(string Path, string Content, string? Message = null) : Synapse;

[GenerateSerializer]
public sealed record ListDirRequest(string Path, string? Description = null) : Synapse;

[GenerateSerializer]
public sealed record DirListResult(string Path, IReadOnlyList<string> Entries, string? Message = null) : Synapse;

[GenerateSerializer]
public sealed record SelfImproveRequest(string Focus = "general") : Synapse;

[GenerateSerializer]
public sealed record ImprovementProposal(
    string ProposalId,
    string Description,
    string SuggestedAction,
    string? CodeHint = null,
    ImprovementAction? StructuredAction = null) : Synapse;

// NeuronOutcome: closed DDD union for handler results (per doc08).
// Replaces ad-hoc strings/nullable outcomes in agents/self-improvers.
// Exhaustive matching in neurons/timeline. Follows exact prior union pattern.
[GenerateSerializer]
public sealed record OutcomeSuccess(Synapse Response);

[GenerateSerializer]
public sealed record OutcomeClarify(string Question);

[GenerateSerializer]
public sealed record OutcomeFailure(string Reason);

[GenerateSerializer]
public sealed record OutcomeInstalled(string ExperienceId);

[GenerateSerializer]
public union NeuronOutcome(OutcomeSuccess, OutcomeClarify, OutcomeFailure, OutcomeInstalled);

[GenerateSerializer]
public sealed record AgentOutcome(NeuronOutcome Outcome) : Synapse;

// U4 Gmail demo (google-auth bundle + D3 capability grants + gmail-last-senders experience).
// Per-brain token isolation, encryption at rest, connector-reads-secret-store (ported scenarios as BDD).
// Auth link uses Hyperlink (decided over Button+OpenUrl to keep client simple; roundtrip in bindings).
[GenerateSerializer]
public sealed record BeginGoogleAuth(string Provider = "google") : Synapse;

[GenerateSerializer]
public sealed record AuthLinkReady(string Url, string Label = "Connect Google") : Synapse;

[GenerateSerializer]
public sealed record GoogleAuthCompleted(string StatusOrTokenHint) : Synapse;

[GenerateSerializer]
public sealed record GmailLastSendersRequest() : Synapse;

[GenerateSerializer]
public sealed record GmailLastSendersResult(string[] Senders) : Synapse;  // concrete array per ser rule

// For rich behavioral visualizations (top senders + counts chart experience).
// Additive; keeps the simple list result for existing gmail-last-senders experience.
[GenerateSerializer]
public sealed record GmailSenderCountsRequest() : Synapse;

[GenerateSerializer]
public sealed record GmailSenderCount(string Sender, int Count);

[GenerateSerializer]
public sealed record GmailSenderCountsResult(GmailSenderCount[] Items) : Synapse;  // concrete array per ser discipline

[GenerateSerializer]
public sealed record CapabilityGrantRequest(string BundleId, string[] Privileges) : Synapse;  // D3: install-time Allow/Deny for privileged (SaveFileRequest etc)

[GenerateSerializer]
public sealed record CapabilityDecision(string BundleId, bool Allowed) : Synapse;  // journaled answer as synapse (amends Q2)

// U5: Upgrade for L3 silo bundles (restarts only the bundle silo resource; brain/kernel/UI stay up per amended Core Law 3).
[GenerateSerializer]
public sealed record UpgradeBundle(string BundleId, string Version) : Synapse;

[GenerateSerializer]
public sealed record BundleUpgraded(string BundleId, string Version, bool Success) : Synapse;

[GenerateSerializer]
public sealed record BundleUninstalled(string BundleId) : Synapse;

[GenerateSerializer]
public sealed record BootManifestApplied([property: Id(0)] string ManifestHash, [property: Id(1)] string World, [property: Id(2)] string[] SeededBundleIds) : Synapse;

[GenerateSerializer]
public sealed record PlacedSurface(
    [property: Id(0)] string SurfaceId,
    [property: Id(1)] string OwnerBundleId,
    [property: Id(2)] int Order,
    [property: Id(3)] bool Pinned);

[GenerateSerializer]
public sealed record RegionPlacement(
    [property: Id(0)] string Region,
    [property: Id(1)] PlacedSurface[] Surfaces);

[GenerateSerializer]
public sealed record WorkspaceState
{
    [Id(0)] public RegionPlacement[] Regions { get; set; } = [];
    [Id(1)] public string Username { get; set; } = "";
    [Id(2)] public WindowState[] Windows { get; set; } = [];
}

[GenerateSerializer]
public sealed record WindowState(
    [property: Id(0)] string WindowId,
    [property: Id(1)] string SurfaceId,
    [property: Id(2)] string Title,
    [property: Id(3)] double X,
    [property: Id(4)] double Y,
    [property: Id(5)] double Width,
    [property: Id(6)] double Height,
    [property: Id(7)] int ZIndex,
    [property: Id(8)] string State);

[GenerateSerializer]
public sealed record PinSurface(string SurfaceId, string Region, int Order) : Synapse;

[GenerateSerializer]
public sealed record UnpinSurface(string SurfaceId) : Synapse;

[GenerateSerializer]
public sealed record MoveSurface(string SurfaceId, string Region, int Order) : Synapse;

[GenerateSerializer]
public sealed record WorkspaceChanged(WorkspaceState Workspace) : Synapse;

[GenerateSerializer]
public sealed record GrantRequested(string BundleId, string[] Capabilities) : Synapse;

[GenerateSerializer]
public sealed record GrantDecision(string BundleId, string[] Capabilities, bool Allowed, string By = "user") : Synapse;

[GenerateSerializer]
public sealed record GrantRevoked(string BundleId, string[] Capabilities) : Synapse;

[GenerateSerializer]
public sealed record BeginTelegramConnect : Synapse;

[GenerateSerializer]
public sealed record UninstallBundle(string BundleId) : Synapse;

[GenerateSerializer]
public sealed record OpenWindow(string SurfaceId, string Title, double X = 10, double Y = 10, double Width = 320, double Height = 400) : Synapse;

[GenerateSerializer]
public sealed record CloseWindow(string WindowId) : Synapse;

[GenerateSerializer]
public sealed record MoveResizeWindow(string WindowId, double X, double Y, double Width, double Height) : Synapse;

[GenerateSerializer]
public sealed record RaiseWindow(string WindowId) : Synapse;

[GenerateSerializer]
public sealed record DockWindow(string WindowId, string Region) : Synapse;

[GenerateSerializer]
public sealed record AutoLayoutWindows() : Synapse;