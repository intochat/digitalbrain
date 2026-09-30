using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Sdk.Integrations.Accounts;

namespace DigitalBrain.Assistant;

// Packages are written by the signed-in principal; the package neuron enforces ownership from the
// stamped caller. Installed apps are keyed inside the caller's brain scope.
internal sealed class PackageService(
    IDigitalBrain brain,
    CSharpToolService files)
{
    public Task<IReadOnlyList<PackageListing>> List() => brain.Get<IPackageDirectory>(PackageDirectory.Key).List();

    public Task<PackageSnapshot> Read(PackageId id) => Package(id).Read();

    public Task<PackageRevision> ReadRevision(PackageId id, string revision) => Package(id).ReadRevision(revision);

    public async Task<PackageRevision> Commit(PackageId id, CommitPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mergeFrom = request.MergeFrom is { } merge ? await Resolve(merge, preferPublished: false) : null;
        var content = request.Content ?? throw new ArgumentException("Package content is required.");
        return await Package(id).Commit(new(request.OperationId ?? Guid.NewGuid(), request.ExpectedHead, content, request.Message, mergeFrom));
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

    public async Task<InstalledPackageView> ReadApp(PackageId id) => await View(await App(id).Read());

    public async Task<PackageAccountOptions> AccountOptions(PackageId id, string? revisionId)
    {
        var revision = await Resolve(new(id.Owner, id.Name, revisionId), preferPublished: true);
        var declared = (await Package(id).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        var scope = CurrentScope();
        var available = ScopedAccounts.Visible(scope, await brain.Get<IIntegrationAccounts>(scope.Id).List());
        return new(revision.Revision, declared.Select(slot => new PackageAccountSlot(slot.Name, slot.Source, slot.Description,
            available.Where(account => account.IntegrationId == slot.Source && account.Status == AccountStatus.Connected)
                .OrderBy(account => account.Id, StringComparer.Ordinal)
                .Select(account => new PackageAccountChoice(account.Id, account.Credential.Label)).ToArray())).ToArray());
    }

    public async Task<InstalledPackageView> Install(PackageId id, InstallPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var revision = await Resolve(new(id.Owner, id.Name, request.Revision), preferPublished: true);
        await RequireActivation(revision);
        await ValidateAccounts(revision, request.Accounts ?? []);
        var app = App(id);
        return await View(await app.Install(new(request.OperationId ?? Guid.NewGuid(), revision, request.Settings ?? [], request.Accounts ?? [])));
    }

    public async Task<InstalledPackageView> Configure(PackageId id, ConfigurePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var app = App(id);
        var existing = await app.Read();
        await RequireActivation(existing.Revision ?? throw new InvalidOperationException("The package is not installed."));
        var selected = new Dictionary<string, string>(existing.Accounts ?? new Dictionary<string, string>());
        foreach (var (slot, account) in request.Accounts ?? []) { selected[slot] = account; }
        await ValidateAccounts(existing.Revision ?? throw new InvalidOperationException("The package is not installed."), selected);
        return await View(await app.Configure(new(request.OperationId ?? Guid.NewGuid(), request.Settings ?? [], request.Accounts ?? [])));
    }

    public async Task<InstalledPackageView> Upgrade(PackageId id, UpgradePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var revision = await Resolve(new(id.Owner, id.Name, request.Revision), preferPublished: true);
        await RequireActivation(revision);
        var existing = (await App(id).Read()).Accounts ?? new Dictionary<string, string>();
        var selected = new Dictionary<string, string>(existing);
        foreach (var (slot, account) in request.Accounts ?? []) { selected[slot] = account; }
        // Slots removed by the new revision are discarded before validation.
        var declared = (await Package(revision.Package).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        selected = selected.Where(pair => declared.Any(slot => slot.Name == pair.Key)).ToDictionary();
        await ValidateAccounts(revision, selected);
        var app = App(id);
        return await View(await app.Upgrade(new(request.OperationId ?? Guid.NewGuid(), revision, selected)));
    }

    public async Task<InstalledPackageView> Uninstall(PackageId id)
    {
        var app = App(id);
        return await View(await app.Uninstall(new(Guid.NewGuid())));
    }

    public Task<AppInvocation> Invoke(PackageId id, InvokePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return App(id).Invoke(new(request.InvocationId ?? Guid.NewGuid(), request.Operation, request.Input));
    }

    public Task<AppInvocation> ReadInvocation(PackageId id, Guid invocationId) => App(id).ReadInvocation(invocationId);

    private async Task<InstalledPackageView> View(AppSnapshot snapshot)
    {
        if (!files.CanRun || snapshot.CSharpFiles.Count == 0) { return new(snapshot, []); }
        var snapshots = new List<CSharpFileSnapshot>(snapshot.CSharpFiles.Count);
        foreach (var file in snapshot.CSharpFiles) { snapshots.Add(await brain.Get<ICSharpFile>(file).Read()); }
        return new(snapshot, snapshots);
    }

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

    private static BrainScope CurrentScope()
    {
        var caller = CallerContextStamper.Require();
        return BrainScope.Create(caller.AccountId, caller.BrainId);
    }

    private IApp App(PackageId id) => brain.Get<IApp>(InstalledPackages.AppKey(id));

    private async Task ValidateAccounts(PackageRevisionRef revision, IReadOnlyDictionary<string, string> selected)
    {
        var declared = (await Package(revision.Package).ReadRevision(revision.Revision)).Content.Manifest.Accounts ?? [];
        if (declared.Count == 0 && selected.Count == 0) { return; }
        var scope = CurrentScope();
        var available = ScopedAccounts.Visible(scope, await brain.Get<IIntegrationAccounts>(scope.Id).List());
        foreach (var slot in declared)
        {
            if (!selected.TryGetValue(slot.Name, out var id) || string.IsNullOrWhiteSpace(id))
            { throw new ArgumentException($"Choose an account for {slot.Name} ({slot.Source})."); }
            var account = available.FirstOrDefault(item => item.Id == id && item.IntegrationId == slot.Source);
            if (account is null || account.Status != AccountStatus.Connected)
            { throw new ArgumentException($"Account {id} is not a connected {slot.Source} account in this workspace."); }
        }
        if (selected.Keys.Any(key => !declared.Any(slot => slot.Name == key)))
        { throw new ArgumentException("The installation contains an undeclared account slot."); }
    }

    // Only a csharp app runs code in the sandbox; apps configured on top of neurons install anywhere.
    private async Task RequireActivation(PackageRevisionRef revision)
    {
        var runtime = (await Package(revision.Package).ReadRevision(revision.Revision)).Content.Manifest.RuntimeName;
        if (runtime != PackageManifest.CSharpRuntime) { return; }
        if (!files.CanRun)
        { throw new InvalidOperationException("This host does not run shared packages. Compose CSharpModule where the brain can run scripts."); }
    }
}
