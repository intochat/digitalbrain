namespace IntoChat.Tests;

/// <summary>
/// P0.1 trash-register guard: the dead server/client code named in the plan stays deleted, the
/// duplicate process runner stays consolidated, and the demo behaviors stay out of the product host.
/// </summary>
public sealed class HygieneFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string PathInRepo(string relative) => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(PathInRepo(relative));

    [Fact]
    public void DeadIntoChatServerFilesAreGone()
    {
        string[] gone =
        [
            "src/Applications/IntoChat/IntoChat/Http/ActivityEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/Http/EdgeResults.cs",
            "src/Applications/IntoChat/IntoChat/Http/GraphEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/Http/NeuronNameFilter.cs",
            "src/Applications/IntoChat/IntoChat/Http/SessionNeuron.cs",
            "src/Applications/IntoChat/IntoChat/Http/SessionStream.cs",
            "src/Applications/IntoChat/IntoChat/Http/SurfaceEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/Http/TableEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/Http/UiEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/Http/WorkspaceAgentTools.cs",
            "src/Applications/IntoChat/IntoChat/Http/WorkspaceArtifactStore.cs",
            "src/Applications/IntoChat/IntoChat/Http/WorkspaceEndpoints.cs",
            "src/Applications/IntoChat/IntoChat/IntoChatHost.cs",
            "src/Modules/AI/AI/ConversationalAgent.cs",
        ];
        foreach (var relative in gone)
        {
            Assert.False(File.Exists(PathInRepo(relative)), $"{relative} is dead code and should be deleted.");
        }
        Assert.False(Directory.Exists(PathInRepo("src/Applications/IntoChat/IntoChat/Http/Projections")), "Http/Projections is dead code and should be deleted.");
    }

    [Fact]
    public void IntoChatProjectNoLongerCompileExcludesFiles()
    {
        Assert.DoesNotContain("Compile Remove", Read("src/Applications/IntoChat/IntoChat/IntoChat.csproj"));
    }

    [Fact]
    public void KernelMcpHostIsDeletedEverywhere()
    {
        Assert.False(Directory.Exists(PathInRepo("src/Modules/DigitalBrain/Kernel/Mcp")), "The Kernel MCP host must be gone from the tree.");
        Assert.DoesNotContain("DigitalBrain.Mcp", Read("DigitalBrain.slnx"));
    }

    [Fact]
    public void EmptyAspireContractsProjectIsDeleted()
    {
        Assert.False(Directory.Exists(PathInRepo("src/Modules/Microsoft/Aspire/Contracts")), "The empty Aspire Contracts project must be gone.");
        Assert.DoesNotContain("Microsoft.Aspire.Contracts", Read("DigitalBrain.slnx"));
        Assert.DoesNotContain("Contracts/DigitalBrain.Modules.Microsoft.Aspire.Contracts.csproj", Read("src/Modules/Microsoft/Aspire/Aspire/DigitalBrain.Modules.Microsoft.Aspire.csproj"));
    }

    [Fact]
    public void StaleReqnrollGitignoreRuleIsGone()
    {
        var gitignore = Read(".gitignore");
        Assert.DoesNotContain("Reqnroll", gitignore, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.feature.cs", gitignore);
    }

    [Fact]
    public void DuplicateProcessRunnerIsConsolidated()
    {
        Assert.False(File.Exists(PathInRepo("src/Modules/Coding/Coding/ProcessRunner.cs")), "The duplicate Coding process runner must be deleted.");
        Assert.False(File.Exists(PathInRepo("src/Modules/Coding/Coding/IProcessRunner.cs")), "The duplicate Coding process-runner interface must be deleted.");
        Assert.False(File.Exists(PathInRepo("src/Modules/Coding/Coding/ProcessResult.cs")), "The duplicate Coding process-result type must be deleted.");
        Assert.True(File.Exists(PathInRepo("src/Modules/Microsoft/DotNet/DotNet/Process/ProcessRunner.cs")), "The single shared process runner must remain under the DotNet module.");
    }

    [Fact]
    public void DemoBehaviorsLeaveTheProductHost()
    {
        Assert.False(File.Exists(PathInRepo("src/Behaviors/ElonBitcoin.cs")), "ElonBitcoin must move to the test host.");
        Assert.False(File.Exists(PathInRepo("src/Behaviors/Fakes/TwitterFakes.cs")), "TwitterFakes must move to the test host.");
        Assert.False(File.Exists(PathInRepo("src/Testing/DigitalBrain.Testing.E2E/Demo/ElonBitcoin.cs")), "The ElonBitcoin inbox feed demo is deleted with the volatile inbox.");
        Assert.True(File.Exists(PathInRepo("src/Testing/DigitalBrain.Testing.E2E/Demo/TwitterFakes.cs")), "TwitterFakes must live in the test host.");
        Assert.DoesNotContain("TestTwitterModule", Read("src/Applications/IntoChat/AppHost/AppHost.cs"));
        Assert.DoesNotContain("AddBehavior<ElonBitcoin>", Read("src/Applications/IntoChat/IntoChat/Behavior/BehaviorEndpoints.cs"));
    }

    [Fact]
    public void HostDownloadsRootIsNotExposedByDefault()
    {
        var appHost = Read("src/Applications/IntoChat/AppHost/AppHost.cs");
        Assert.DoesNotContain("Downloads", appHost);
        Assert.Contains("IntoChat:LocalFiles:Roots:downloads", appHost);
    }

    [Fact]
    public void ProductProfileGatesDeveloperOnlyModules()
    {
        var appHost = Read("src/Applications/IntoChat/AppHost/AppHost.cs");
        var gate = appHost.IndexOf("developerProfile", StringComparison.Ordinal);
        Assert.True(gate >= 0, "The AppHost must select a product/developer profile.");
        foreach (var module in new[] { "WithModule<RoslynModule>", "WithModule<DotNetModule>", "WithModule<CodingModule>", "WithModule<BehaviorModule>" })
        {
            var index = appHost.IndexOf(module, StringComparison.Ordinal);
            Assert.True(index > gate, $"{module} is developer-only and must be gated behind the developer profile.");
        }
    }

    [Fact]
    public void LegacyVolatileInboxIsDeleted()
    {
        string[] gone =
        [
            "src/Modules/Google/Flutter/Contracts/Inbox/IInbox.cs",
            "src/Modules/Google/Flutter/Contracts/Inbox/Signals/InboxAppeared.cs",
            "src/Modules/Google/Flutter/Flutter/Inbox/InboxNeuron.cs",
            "src/Modules/Google/Flutter/Flutter/Inbox/InboxEndpoints.cs",
        ];
        foreach (var relative in gone)
        {
            Assert.False(File.Exists(PathInRepo(relative)), $"{relative} is the volatile inbox and should be deleted.");
        }
        var module = Read("src/Modules/Google/Flutter/Flutter/FlutterModule.cs");
        Assert.DoesNotContain("InboxPath", module);
        Assert.DoesNotContain("MapInbox", module);
    }

    [Fact]
    public void DeadDartFilesAreGone()
    {
        string[] gone =
        [
            "src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_programs.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/artifact_editors.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_table_import.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/brain_graph_store.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_brain_observation.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/graph_artifact.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_voice.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/voice_file_io.dart",
            "src/Modules/Google/Flutter/app/shell/lib/workspace/voice_file_web.dart",
            "src/Modules/Google/Flutter/app/shell/lib/inbox_banner.dart",
            "src/Modules/Google/Flutter/app/shell/test/inbox_banner_test.dart",
        ];
        foreach (var relative in gone)
        {
            Assert.False(File.Exists(PathInRepo(relative)), $"{relative} is dead client code and should be deleted.");
        }
    }

    [Fact]
    public void DartShellNoLongerReferencesDeletedFeatures()
    {
        var workspaceApp = Read("src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_app.dart");
        foreach (var symbol in new[] { "InboxBanner", "WorkspacePrograms", "BrainGraphStore", "WorkspaceArtifactEditor", "importWorkspaceTable", "workspaceBrainObservation" })
        {
            Assert.DoesNotContain(symbol, workspaceApp);
        }
        Assert.DoesNotContain("/programs", Read("src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_routes.dart"));

        var client = Read("src/Modules/Google/Flutter/app/core/lib/src/ui_client.dart");
        foreach (var symbol in new[] { "programmingRequest", "transcribeVoice", "createWorkspaceArtifact", "readWorkspaceArtifact", "updateWorkspaceArtifact", "listWorkspaceArtifacts" })
        {
            Assert.DoesNotContain(symbol, client);
        }
    }
}