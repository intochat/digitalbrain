using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Connectors;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Packages;

// Packages are written by the signed-in principal; the package neuron enforces ownership from the
// stamped caller. Installed apps are keyed inside the caller's workspace scope.
internal sealed class PackageService(
    IDigitalBrain brain,
    IOptions<BasicAuthOptions> auth,
    IOptions<BehaviorAuthoringOptions> authoring,
    IOptions<CodeExecutionOptions> coding,
    IOptions<BehaviorOptions> runtime)
{
    private static readonly TimeSpan CheckPollInterval = TimeSpan.FromMilliseconds(250);

    public Task<IReadOnlyList<PackageListing>> List() => brain.Get<IPackageDirectory>(PackageDirectory.Key).List();

    public Task<PackageSnapshot> Read(PackageId id) => Package(id).Read();

    public Task<PackageRevision> ReadRevision(PackageId id, string revision) => Package(id).ReadRevision(revision);

    public async Task<PackageRevision> Commit(PackageId id, CommitPackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireCoding();
        var mergeFrom = request.MergeFrom is { } merge ? await Resolve(merge, preferPublished: false) : null;
        var check = await Check(request.Content ?? throw new ArgumentException("Package content is required."), cancellationToken);
        if (check.Artifact is null) { throw new PackageCheckFailedException(check); }
        return await Package(id).Commit(new(request.OperationId ?? Guid.NewGuid(), request.ExpectedHead, request.Content, check.Artifact, request.Message, mergeFrom));
    }

    public async Task<PackageSnapshot> Fork(PackageId source, ForkPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = PackageId.Create(CallerContextStamper.Require().PrincipalId, request.Name ?? source.Name);
        var revision = await Resolve(new(source.Owner, source.Name, request.Revision), preferPublished: true);
        return await Package(target).Fork(new(request.OperationId ?? Guid.NewGuid(), revision));
    }

    public async Task<PackageSnapshot> Pull(PackageId id, PullPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source is { } named
            ? await Resolve(named, preferPublished: true)
            : (await Package(id).Read()).ForkedFrom is { } origin
                ? await Latest(origin.Package, preferPublished: true)
                : throw new ArgumentException($"{id} is not a fork; name the package to pull from.");
        return await Package(id).Pull(new(request.OperationId ?? Guid.NewGuid(), source));
    }

    public async Task<PackageProposal> Propose(PackageId upstream, ProposePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = await Resolve(request.Source ?? throw new ArgumentException("Name the fork to propose from."), preferPublished: false);
        return await Package(upstream).Propose(new(request.OperationId ?? Guid.NewGuid(), source, request.Title));
    }

    public Task<PackageSnapshot> Accept(PackageId id, int number) => Package(id).Accept(new(Guid.NewGuid(), number));

    public Task<PackageSnapshot> Close(PackageId id, int number) => Package(id).Close(new(Guid.NewGuid(), number));

    public async Task<PackageSnapshot> Publish(PackageId id, PublishPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var revision = request.Revision ?? (await Package(id).Read()).Head ?? throw new InvalidOperationException($"{id} has no revision to publish.");
        return await Package(id).Publish(new(request.OperationId ?? Guid.NewGuid(), revision));
    }

    public async Task<InstalledPackageView> ReadApp(string workspaceId, PackageId id) => await View(await App(workspaceId, id).Read());

    public async Task<SynapseAccountOptions> AccountOptions(string workspaceId, PackageId id, string? revisionId)
    {
        var revision = await Resolve(new(id.Owner, id.Name, revisionId), preferPublished: true);
        var declared = (await Package(id).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        var scope = WorkspaceScope.Current(auth.Value, workspaceId).Id;
        var owner = CallerContextStamper.Require().PrincipalId;
        var available = await brain.Get<IConnectors>(owner).List();
        return new(revision.Revision, declared.Select(slot => new SynapseAccountSlot(slot.Name, slot.Source, slot.Description,
            available.Where(account => account.Source == slot.Source && account.Status == ConnectorStatus.Connected
                && (account.WorkspaceId == scope || account.WorkspaceId == owner))
                .OrderBy(account => account.Id, StringComparer.Ordinal)
                .Select(account => new SynapseAccountChoice(account.Id, account.Credential.Label)).ToArray())).ToArray());
    }

    public async Task<InstalledPackageView> Install(string workspaceId, PackageId id, InstallPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActivation();
        var revision = await Resolve(new(id.Owner, id.Name, request.Revision), preferPublished: true);
        await ValidateAccounts(workspaceId, revision, request.Accounts ?? []);
        var app = App(workspaceId, id);
        return await View(await app.Install(new(request.OperationId ?? Guid.NewGuid(), revision, request.Settings ?? [], request.Accounts ?? [])));
    }

    public async Task<InstalledPackageView> Configure(string workspaceId, PackageId id, ConfigurePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActivation();
        var app = App(workspaceId, id);
        var existing = await app.Read();
        var selected = new Dictionary<string, string>(existing.Accounts ?? new Dictionary<string, string>());
        foreach (var (slot, account) in request.Accounts ?? []) { selected[slot] = account; }
        await ValidateAccounts(workspaceId, existing.Revision ?? throw new InvalidOperationException("The package is not installed."), selected);
        return await View(await app.Configure(new(request.OperationId ?? Guid.NewGuid(), request.Settings ?? [], request.Accounts ?? [])));
    }

    public async Task<InstalledPackageView> Upgrade(string workspaceId, PackageId id, UpgradePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireActivation();
        var revision = await Resolve(new(id.Owner, id.Name, request.Revision), preferPublished: true);
        var existing = (await App(workspaceId, id).Read()).Accounts ?? new Dictionary<string, string>();
        var selected = new Dictionary<string, string>(existing);
        foreach (var (slot, account) in request.Accounts ?? []) { selected[slot] = account; }
        // Slots removed by the new revision are discarded before validation.
        var declared = (await Package(revision.Package).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        selected = selected.Where(pair => declared.Any(slot => slot.Name == pair.Key)).ToDictionary();
        await ValidateAccounts(workspaceId, revision, selected);
        var app = App(workspaceId, id);
        return await View(await app.Upgrade(new(request.OperationId ?? Guid.NewGuid(), revision, selected)));
    }

    public async Task<InstalledPackageView> Uninstall(string workspaceId, PackageId id)
    {
        var app = App(workspaceId, id);
        return await View(await app.Uninstall(new(Guid.NewGuid())));
    }

    public Task<AppInvocation> Invoke(string workspaceId, PackageId id, InvokePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return App(workspaceId, id).Invoke(new(request.InvocationId ?? Guid.NewGuid(), request.Operation, request.Input));
    }

    public Task<AppInvocation> ReadInvocation(string workspaceId, PackageId id, Guid invocationId) => App(workspaceId, id).ReadInvocation(invocationId);

    // Identical content shares one draft, so a retried commit reuses its passing check instead of rebuilding.
    private async Task<CodeCheckSnapshot> Check(PackageContent content, CancellationToken cancellationToken)
    {
        var draftKey = "package-check-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content)));
        var draft = brain.Get<ICodeDraft>(draftKey);
        var saved = await draft.Read(cancellationToken);
        if (saved.Revision == 0)
        { saved = await draft.Save(new(0, Guid.NewGuid(), content.Source, content.Tests, content.ModuleIds ?? []), cancellationToken); }
        var check = saved.LatestCheckId is { } latest ? await draft.ReadCheck(latest, cancellationToken) : null;
        // Only a passing or running check is reused; a failure may have been a timeout, so asking again rebuilds.
        if (check?.Status is not (CodeCheckStatus.Passed or CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing))
        { check = await draft.Check(new(saved.Revision, Guid.NewGuid()), cancellationToken); }
        while (check.Status is CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing)
        {
            await Task.Delay(CheckPollInterval, cancellationToken);
            check = await draft.ReadCheck(check.OperationId, cancellationToken);
        }
        return check;
    }

    private async Task<InstalledPackageView> View(AppSnapshot snapshot)
        => new(snapshot, snapshot.BehaviorProgram is { } program && HasRuntime ? await brain.Get<IBehaviorProgram>(program).Read() : null);

    private async Task<PackageRevisionRef> Resolve(PackageReference reference, bool preferPublished)
    {
        var id = PackageId.Create(reference.Owner, reference.Name);
        return reference.Revision is { } revision ? new(id, revision) : await Latest(id, preferPublished);
    }

    private async Task<PackageRevisionRef> Latest(PackageId id, bool preferPublished)
    {
        var snapshot = await Package(id).Read();
        var revision = (preferPublished ? snapshot.Published ?? snapshot.Head : snapshot.Head) ?? throw new KeyNotFoundException($"{id} has no revisions yet.");
        return new(id, revision);
    }

    private IPackage Package(PackageId id) => brain.Get<IPackage>(id.ToString());

    private IApp App(string workspaceId, PackageId id) => brain.Get<IApp>(WorkspaceScope.Current(auth.Value, workspaceId).Id + "/packages/" + id);

    private async Task ValidateAccounts(string workspaceId, PackageRevisionRef revision, IReadOnlyDictionary<string, string> selected)
    {
        var declared = (await Package(revision.Package).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        if (declared.Count == 0 && selected.Count == 0) { return; }
        var scope = WorkspaceScope.Current(auth.Value, workspaceId).Id;
        var owner = CallerContextStamper.Require().PrincipalId;
        var available = await brain.Get<IConnectors>(owner).List();
        foreach (var slot in declared)
        {
            if (!selected.TryGetValue(slot.Name, out var id) || string.IsNullOrWhiteSpace(id))
            { throw new ArgumentException($"Choose an account for {slot.Name} ({slot.Source})."); }
            var account = available.FirstOrDefault(item => item.Id == id && item.WorkspaceId is not null
                && (item.WorkspaceId == scope || item.WorkspaceId == owner) && item.Source == slot.Source);
            if (account is null || account.Status != ConnectorStatus.Connected)
            { throw new ArgumentException($"Account {id} is not a connected {slot.Source} account in this workspace."); }
        }
        if (selected.Keys.Any(key => !declared.Any(slot => slot.Name == key)))
        { throw new ArgumentException("The installation contains an undeclared account slot."); }
    }

    private bool HasCoding => !string.IsNullOrWhiteSpace(coding.Value.Root);

    private bool HasRuntime => !string.IsNullOrWhiteSpace(runtime.Value.Root);

    private void RequireCoding()
    {
        if (!HasCoding) { throw new InvalidOperationException("This host does not check packages; use the developer profile with a behavior authoring root."); }
    }

    private void RequireActivation()
    {
        if (!HasCoding || !HasRuntime || !authoring.Value.AllowActivation)
        { throw new InvalidOperationException("This host does not run shared packages. Enable IntoChat:BehaviorAuthoring:AllowActivation in the developer profile."); }
    }
}
