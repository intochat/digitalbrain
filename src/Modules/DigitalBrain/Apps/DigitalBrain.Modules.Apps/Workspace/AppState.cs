namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.app-state")]
public sealed record AppState
{
    [Id(0)] public AppStatus Status { get; init; }
    [Id(1)] public PackageRevisionRef? Revision { get; init; }
    [Id(3)] public List<PackageSetting> Declared { get; init; } = [];
    [Id(4)] public Dictionary<string, string> Settings { get; init; } = [];
    [Id(5)] public List<PackageOperation> Operations { get; init; } = [];
    [Id(6)] public int ProgramGeneration { get; init; }
    [Id(7)] public List<AppInvocation> Invocations { get; init; } = [];
    [Id(8)] public List<OperationReceipt> Receipts { get; init; } = [];
    [Id(9)] public Dictionary<string, string> Accounts { get; init; } = [];
    [Id(10)] public string Runtime { get; init; } = PackageManifest.CSharpRuntime;
    // The program paths the current generation deployed, so retiring needs no second look at the revision.
    [Id(11)] public string[] ScriptPaths { get; init; } = [];
    [Id(12)] public string[] StorageFiles { get; init; } = [];
    [Id(13)] public AppDeployment? PendingDeployment { get; init; }
    [Id(14)] public UninstallApp? PendingUninstall { get; init; }
    [Id(15)] public bool StorageHistoryKnown { get; init; }
    [Id(16)] public OperationReceipt[] LifecycleReceipts { get; init; } = [];
    public bool RunsScript => Runtime == PackageManifest.CSharpRuntime;
}

[GenerateSerializer]
public sealed record AppDeployment([property: Id(0)] AppState Target);
