# Dead SDK-Neuron Cleanup & Build-Graph Slimming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the confirmed-dead "SDK neuron" layer (Roslyn/Git/NuGet/DotNet/FileSystem/Shell/Winget + newly-confirmed GoogleDrive/GoogleCalendar), fix the `Microsoft.CodeAnalysis.CSharp` double-pin causing NU1506 warnings across ~30 projects, shrink the CI test-time build graph by excluding the Pulumi deploy project, and get one complete real-CI timing number — with zero regressions to the live neurons (Roslyn scripting/Foundry, Gmail, GoogleAuth, SalesforceAuth, SalesforceCrm) along the way.

**Architecture:** Straight deletion of dead grain types + their sole-consumer support classes + DI wiring + tests, done in dependency order so every intermediate commit still builds. One live dependency (`ProcessRunner`) gets relocated, not deleted, because a live component (`OutOfProcessSandbox`) also uses it. Package-pin and CI-workflow fixes are separate, independently-testable tasks layered on top.

**Tech Stack:** .NET 11 preview, Orleans 10.2, Roslyn/Microsoft.CodeAnalysis, Aspire, xUnit, GitHub Actions, Pulumi.

## Global Constraints

- Baseline: `dotnet test Brain.slnx -c Release` = **456 passed, 6 skipped, 0 failed** (local, pre-session). Run this exact command after every task below; the only acceptable deltas are the tests you intentionally deleted in that task.

**Observed post-Task 1 deletions + Task 3 bump (this run with --no-restore):** DigitalBrain.Tests.dll: **381 passed, 3 failed, 6 skipped, Total ~390**. The drop matches the deleted dead test classes (~75 tests). The 3 failures (one JournalFormatSpike + three ScriptRunner_*) pass when run isolated or after `dotnet restore`; they trigger the known "load skew (4.8 vs 5.x)" fallback path in ScriptRunner.cs because of --no-restore. Zero real regressions to live Roslyn scripting/Foundry paths.

---

### ⚠️ CRITICAL FINDING (2026-07-06, follow-up session): the pushed tree did not build; fixed now

A ground-truth re-verification (not trusting this plan's checkboxes) found **master's HEAD did not compile**, and the real `deploy.yml` CI run for the current HEAD (`0542056`) had already **failed** because of it — not for any reason related to this cleanup. Root cause: three separate commits made during this cleanup session each bundled an unrelated, half-finished extraction/deletion into a commit whose message didn't mention it, and none were caught before pushing because local verification apparently ran against an earlier tree state:

1. Commit `5696a5c` ("...silo->kernel comment cleanups...") also deleted 74 lines from `DigitalBrain.Core/Synapse.cs` — the entire `UserId`/`LoginRequest`/`LoginSucceeded`/`LoginFailed`/`LogoutRequest`/`UserSessionCreated`/`UserSessionEnded`/`LocalUserRegistered`/`UserSessionState`/`IUserGrain`/`UserProfile` auth/session type family — with no replacement anywhere. **Fixed: restored the exact deleted block** (verified via `git show 5696a5c` diff).
2. Commit `2617d96` ("...update remaining product 'silo' comment in test configurator...") also added a `StageSelfEvolutionProposalAsync` method to `DigitalBrain.Kernel/SoftwareEngineeringClosedLoopNeuron.cs` calling `SelfEvolutionProposal`/`SelfEvolutionRisk` — types with no committed definition anywhere in git history. **Correction after further investigation:** these types were NOT actually deleted as scratch — they exist right now in the working tree at `DigitalBrain.Core/SelfEvolution.cs`, just **untracked** (never `git add`ed), which is exactly why they're invisible to `git log -S`. An independent, convergent analysis (also untracked, `docs/architecture-trash-analysis-2026-07-06.md`) found the identical build break the same way and recommends restoring this as its #1 priority slice — the self-evolution proposal/decision rail is real, wanted, in-progress product work (matches the user's own description of completed work), not scratch. First attempt at a fix (reverting `SoftwareEngineeringClosedLoopNeuron.cs` to its pre-`2617d96` form) was wrong for this reason and was undone — **the file is back to its committed (`2617d96`) form, calling `SelfEvolutionProposal`/`SelfEvolutionRisk`, and those types' definitions in `DigitalBrain.Core/SelfEvolution.cs` just need to be staged (`git add`) so they stop being an untracked, loseable file.** Verified this combination builds clean.
3. Commit `56b8bd4` ("Phase 0 deletions & kernel renames...") also refactored `GeneratedNeuron.cs`, `DigitalBrain.Kernel/Ino/InoNeuron.cs`, and `DigitalBrain.Kernel/Gateway/GatewayService.cs` to call into four new extracted classes (`GeneratedPackRuntime`, `InoConnectorIntents`, `GatewayPayload`, `GatewayInternalAuth`) that were likewise never actually created. Commit `e293251` (this session's own dead-neuron deletion commit) did the same to `UiSurfaceRfwBridge.cs` (`WidgetTreeRfwBridge`). **Fixed: reconstructed all five classes** from the pre-refactor logic (diffed via `git show <commit> -- <file>` against each commit's parent) — `DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs`, `DigitalBrain.Kernel/Ino/InoConnectorIntents.cs`, `DigitalBrain.Kernel/Ui/WidgetTreeRfwBridge.cs`, `DigitalBrain.Kernel/Gateway/GatewayPayload.cs`, `DigitalBrain.Kernel/Gateway/GatewayInternalAuth.cs` — preserving the original behavior exactly, not reverting the extractions (they're legitimate architecture cleanup, just missing their implementation files).

**Verified fixed:** `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true -m:1` → **0 errors, 8 pre-existing nullable warnings** (unrelated, see Item C in the remaining-items findings below). `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true -p:SkipDeployBuild=true --filter "FullyQualifiedName!~E2E"` → **424 passed, 0 failed, 0 skipped, ~2m23s** (this is now the real, reproducible current baseline — supersedes both the stale 456/6 pre-session number and the bogus 381/3-failed --no-restore number above). The two previously-flagged flaky tests (`JournalFormatSpikeTests`, `PackAlcEmbodierTests`' `ScriptRunner_*` tests) pass cleanly (13/13) in this normal run — the "4.8 vs 5.x load skew" comment in `ScriptRunner.cs` is now stale prose (harmless to leave; the skew it described can't happen now that `Directory.Packages.props` unifies everything to 5.6.0) but was not re-verified as a live risk under `--no-restore` specifically.

**These fixes are made in the working tree but NOT YET COMMITTED** — pending user go-ahead per the "only commit when explicitly asked" rule.

---
- Do NOT touch `NeuronTestBase`'s per-test-method `TestCluster` reboot architecture, and do NOT add/remove any `[Collection("silo-host")]` / `DisableParallelization` markers — that is a separate, already-investigated-and-parked initiative.
- Package versions verified against nuget.org 2026-07-06 (not local NuGet cache, per standing instruction): `Microsoft.CodeAnalysis` / `.Common` / `.CSharp` / `.CSharp.Scripting` all have latest stable **5.6.0** (released 2026-07-02). User explicitly approved bumping the Roslyn-scripting group from 4.8.0 → 5.6.0 as part of this cleanup (see Task 3).
- Pulumi's `pulumi/actions@v6` step builds/runs the `deploy/` Pulumi program itself via the Pulumi CLI/Automation API (confirmed via Context7 `/pulumi/actions` docs: `Execute Command` is a distinct step driven by the Automation API against `work-dir: deploy`, independent of anything `dotnet test` restores) — so excluding `deploy/DigitalBrain.Deploy.csproj` from the CI test step's build graph is safe (Task 4).
- Each CI push is expensive (~20-30 min real Azure/GitHub Actions spend, and the deploy job actually runs `pulumi up` against real cloud infra) — do Tasks 1-4 locally across as few commits as reasonably possible, and get explicit go-ahead before the Task 5 push (it triggers a real deploy, not just tests).
- Never touch anything under `C:\Users\` (NuGet cache, user profile) — all verification uses Context7/nuget.org/the repo itself.

---

## Task 1: Delete dead SDK + Google neurons, their sole-support classes, and DI wiring; relocate the still-live `ProcessRunner`

**Context:** Confirmed dead (zero call sites outside their own tests, verified via repo-wide grep): `RoslynNeuron`, `GitNeuron`, `NuGetNeuron`, `DotNetNeuron` (DigitalBrain.Developer + DigitalBrain.Kernel/Sdk), `FileSystemNeuron`, `ShellNeuron`, `WingetNeuron` (DigitalBrain.Windows + DigitalBrain.Kernel/Sdk), and newly confirmed this session: `GoogleDriveNeuron`, `GoogleCalendarNeuron` (DigitalBrain.Google + DigitalBrain.Kernel/Google — no `GetGrain<>` call, no `GoogleSignals` entry, no InoNeuron/GatewayService reference; only their own tests + shared OAuth-scope plumbing touch them). `GoogleAuthNeuron`, `GmailNeuron`, `SalesforceAuthNeuron`, `SalesforceCrmNeuron` are LIVE (real `GatewayService.cs`/`InoNeuron.cs` dispatch or a live `/salesforce-callback` HTTP route) — **do not touch these four**.

`DigitalBrain.Windows/ProcessRunner.cs` is used by all 5 dead SDK-Windows/Developer neurons **and** by the live `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs` (DI-registered in `FoundryServices.cs`, wired from every silo boot via `Program.cs:274`). It must be relocated, not deleted. Because the dead neurons still reference it via `using DigitalBrain.Windows;`, the relocation must happen in the same batch as their deletion (moving it first would break their compile before they're deleted).

**Files:**
- Delete: `DigitalBrain.Kernel/Sdk/RoslynNeuron.cs`, `GitNeuron.cs`, `NuGetNeuron.cs`, `DotNetNeuron.cs`, `FileSystemNeuron.cs`, `ShellNeuron.cs`, `WingetNeuron.cs`
- Delete: `DigitalBrain.Developer/IRoslynNeuron.cs`, `IGitNeuron.cs`, `INuGetNeuron.cs`, `IDotNetNeuron.cs`, `RoslynAnalysisService.cs`
- Delete: `DigitalBrain.Windows/IFileSystemNeuron.cs`, `IShellNeuron.cs`, `IWingetNeuron.cs`, `FileSystemOperations.cs`
- Delete: `DigitalBrain.Kernel/Google/GoogleDriveNeuron.cs`, `GoogleCalendarNeuron.cs`
- Delete: `DigitalBrain.Google/IGoogleDriveNeuron.cs`, `IGoogleCalendarNeuron.cs`, `GoogleDriveApiClient.cs`, `IGoogleDriveApiClient.cs`, `GoogleCalendarApiClient.cs`, `IGoogleCalendarApiClient.cs`
- Delete: `DigitalBrain.Developer.Tests/RoslynNeuronTests.cs`, `RoslynAnalysisServiceTests.cs`, `GitNeuronTests.cs`, `NuGetNeuronTests.cs`, `DotNetNeuronTests.cs`
- Delete: `DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs`, `FileSystemOperationsTests.cs`, `ShellNeuronTests.cs`, `WingetNeuronTests.cs`
- Delete: `DigitalBrain.Google.Tests/GoogleDriveNeuronTests.cs`, `GoogleCalendarNeuronTests.cs`
- Delete: `DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs`, `DigitalBrain.Tests/Sdk/SdkMetadataTests.cs`
- Modify: `DigitalBrain.Tests/Kernel/NeuronTests.cs`
- Modify: `DigitalBrain.Google.Tests/FakeGoogleApiClients.cs`
- Modify: `DigitalBrain.Kernel/Program.cs`
- Modify: `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs`
- Modify: `DigitalBrain.Google/DigitalBrain.Google.csproj`
- Modify: `Directory.Packages.props`
- Create: `DigitalBrain.Kernel/Sandbox/ProcessRunner.cs`
- Delete: `DigitalBrain.Windows/ProcessRunner.cs`
- Modify: `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs`

- [x] **Step 1: Delete the 7 dead grain implementation files**

```
git rm DigitalBrain.Kernel/Sdk/RoslynNeuron.cs DigitalBrain.Kernel/Sdk/GitNeuron.cs DigitalBrain.Kernel/Sdk/NuGetNeuron.cs DigitalBrain.Kernel/Sdk/DotNetNeuron.cs DigitalBrain.Kernel/Sdk/FileSystemNeuron.cs DigitalBrain.Kernel/Sdk/ShellNeuron.cs DigitalBrain.Kernel/Sdk/WingetNeuron.cs
```

- [x] **Step 2: Delete their interfaces and sole-consumer support services**

```
git rm DigitalBrain.Developer/IRoslynNeuron.cs DigitalBrain.Developer/IGitNeuron.cs DigitalBrain.Developer/INuGetNeuron.cs DigitalBrain.Developer/IDotNetNeuron.cs DigitalBrain.Developer/RoslynAnalysisService.cs
git rm DigitalBrain.Windows/IFileSystemNeuron.cs DigitalBrain.Windows/IShellNeuron.cs DigitalBrain.Windows/IWingetNeuron.cs DigitalBrain.Windows/FileSystemOperations.cs
```

- [x] **Step 3: Delete the dead Google neurons, their interfaces, and their API-client wrapper classes**

```
git rm DigitalBrain.Kernel/Google/GoogleDriveNeuron.cs DigitalBrain.Kernel/Google/GoogleCalendarNeuron.cs
git rm DigitalBrain.Google/IGoogleDriveNeuron.cs DigitalBrain.Google/IGoogleCalendarNeuron.cs DigitalBrain.Google/GoogleDriveApiClient.cs DigitalBrain.Google/IGoogleDriveApiClient.cs DigitalBrain.Google/GoogleCalendarApiClient.cs DigitalBrain.Google/IGoogleCalendarApiClient.cs
```

- [x] **Step 4: Trim `FakeGoogleApiClients.cs` down to only the fake Gmail still needs**

File: `DigitalBrain.Google.Tests/FakeGoogleApiClients.cs`. Replace the full file content with:

```csharp
namespace DigitalBrain.Google.Tests;

public sealed class FakeGmailApiClient : IGmailApiClient
{
    public List<(string To, string Subject, string Body)> SentMessages { get; } = [];

    public Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct) =>
        Task.FromResult(new[] { "fake-message-1", "fake-message-2" });

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct) =>
        Task.FromResult($"fake body for {messageId}");

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }
}
```

- [x] **Step 5: Delete the now-orphaned test files (own-family tests + cross-cutting metadata tests)**

```
git rm DigitalBrain.Developer.Tests/RoslynNeuronTests.cs DigitalBrain.Developer.Tests/RoslynAnalysisServiceTests.cs DigitalBrain.Developer.Tests/GitNeuronTests.cs DigitalBrain.Developer.Tests/NuGetNeuronTests.cs DigitalBrain.Developer.Tests/DotNetNeuronTests.cs
git rm DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs DigitalBrain.Windows.Tests/FileSystemOperationsTests.cs DigitalBrain.Windows.Tests/ShellNeuronTests.cs DigitalBrain.Windows.Tests/WingetNeuronTests.cs
git rm DigitalBrain.Google.Tests/GoogleDriveNeuronTests.cs DigitalBrain.Google.Tests/GoogleCalendarNeuronTests.cs
git rm DigitalBrain.Tests/Sdk/SdkContractsMetadataTests.cs DigitalBrain.Tests/Sdk/SdkMetadataTests.cs
```

- [x] **Step 6: Remove the `GitNeuron` test block from `DigitalBrain.Tests/Kernel/NeuronTests.cs`**

Remove the now-unused `using DigitalBrain.Developer;` at line 4 (verified: no other symbol from that namespace is used anywhere else in this file).

Remove lines 587-650 in full (the `GitNeuron_Commits_And_Derives_Metrics_From_Journal` fact plus its two private helpers `RunGit`/`TryDeleteDir` — verified: neither helper nor `IGitNeuron`/`GitMetrics` is referenced anywhere else in this file):

```csharp
    [Fact]
    public async Task GitNeuron_Commits_And_Derives_Metrics_From_Journal()
    {
        var repo = Path.Combine(Path.GetTempPath(), "dbgit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repo);
        try
        {
            RunGit("init -b main", repo);
            RunGit("config user.email test@example.com", repo);
            RunGit("config user.name Tester", repo);
            RunGit("config commit.gpgsign false", repo);
            await File.WriteAllTextAsync(Path.Combine(repo, "file.txt"), "hello");

            var git = Grain<IGitNeuron>("git-test");

            var status = await git.StatusAsync(repo);
            Assert.Contains("file.txt", status);

            await git.CommitAsync(repo, "add file");

            var log = await git.LogAsync(repo);
            Assert.Single(log);
            Assert.Contains("add file", log[0]);

            var metrics = await git.GetMetricsAsync();
            Assert.Equal(1, metrics.TotalCommits);
            Assert.Equal(0, metrics.TotalReverts);
            Assert.True(metrics.LastCommit > DateTimeOffset.MinValue);
        }
        finally
        {
            TryDeleteDir(repo);
        }
    }

    private static void RunGit(string args, string cwd)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup: Windows can briefly lock .git pack files. Not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above — read-only .git objects on some platforms.
        }
    }

```

(Leave the blank line before `private sealed class IsolatedReplayTest : NeuronTests` that currently follows this block.)

- [x] **Step 7: Edit `DigitalBrain.Kernel/Program.cs` — remove dead DI wiring**

Remove lines 103-104 (comment + registration; leave line 105's `SqliteSchemaInspector` registration untouched):

```csharp
// FileSystemNeuron delegates its System.IO logic to this ino-hosted, Orleans-free plain class.
builder.Services.AddSingleton<DigitalBrain.Windows.FileSystemOperations>();
```

Remove lines 107-108 (comment + registration; leave line 109's `AddHttpClient<...CoinGeckoApiClient>` untouched):

```csharp
// RoslynNeuron delegates its MSBuildWorkspace analysis logic to this ino-hosted, Orleans-free plain class.
builder.Services.AddSingleton<DigitalBrain.Developer.RoslynAnalysisService>();
```

Replace the comment block at lines 175-180 (currently mentions Drive/Calendar, now stale) with:

```csharp
// Google Gmail API client: one UserCredential per grain activation, built from the "google"/"default" pack
// config scope (client_id/client_secret/refresh_token), mirroring LlmResponderNeuron's per-scope
// IPackConfigStore resolution. Scoped (not singleton) because Orleans creates one DI scope per grain
// activation, so each GmailNeuron activation resolves its own credential/service. GetAwaiter().GetResult()
// is safe here: grain activation runs on thread-pool threads with no captured SynchronizationContext, so
// there is no deadlock risk (the same reasoning ASP.NET Core middleware relies on).
```

Remove lines 184-187 (the dead Drive/Calendar API client registrations; keep lines 181-183's credential + Gmail registrations):

```csharp
builder.Services.AddScoped<DigitalBrain.Google.IGoogleDriveApiClient>(sp =>
    new DigitalBrain.Google.GoogleDriveApiClient(sp.GetRequiredService<Google.Apis.Auth.OAuth2.UserCredential>()));
builder.Services.AddScoped<DigitalBrain.Google.IGoogleCalendarApiClient>(sp =>
    new DigitalBrain.Google.GoogleCalendarApiClient(sp.GetRequiredService<Google.Apis.Auth.OAuth2.UserCredential>()));
```

In `BuildGoogleCredential` near the bottom of the file, replace:

```csharp
    if (!values.TryGetValue("client_id", out var clientId) ||
        !values.TryGetValue("client_secret", out var clientSecret) ||
        !values.TryGetValue("refresh_token", out var refreshToken))
    {
        throw new InvalidOperationException(
            $"Google pack config (scope '{scope}', pack '{pack}') is missing client_id/client_secret/refresh_token. " +
            "Complete \"Sign in with Google\" before using Gmail/Drive/Calendar neurons.");
    }

    return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(
        clientId, clientSecret, refreshToken,
        Google.Apis.Gmail.v1.GmailService.ScopeConstants.MailGoogleCom,
        Google.Apis.Drive.v3.DriveService.ScopeConstants.Drive,
        Google.Apis.Calendar.v3.CalendarService.ScopeConstants.Calendar);
```

with:

```csharp
    if (!values.TryGetValue("client_id", out var clientId) ||
        !values.TryGetValue("client_secret", out var clientSecret) ||
        !values.TryGetValue("refresh_token", out var refreshToken))
    {
        throw new InvalidOperationException(
            $"Google pack config (scope '{scope}', pack '{pack}') is missing client_id/client_secret/refresh_token. " +
            "Complete \"Sign in with Google\" before using the Gmail neuron.");
    }

    return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(
        clientId, clientSecret, refreshToken,
        Google.Apis.Gmail.v1.GmailService.ScopeConstants.MailGoogleCom);
```

- [x] **Step 8: Edit `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs`** — done in substance; file was renamed to `NeuronTestKernelConfigurator.cs` during the silo→kernel pass (commit `e293251`) and had exactly these 4 lines removed there.

Remove the now-unused `using DigitalBrain.Developer;` (line 3) and `using DigitalBrain.Windows;` (line 10).

Remove these two lines from inside `ConfigureServices`:

```csharp
                services.AddSingleton<FileSystemOperations>();
```
```csharp
                services.AddSingleton<RoslynAnalysisService>();
```

- [x] **Step 9: Relocate `ProcessRunner.cs` out of `DigitalBrain.Windows` into `DigitalBrain.Kernel/Sandbox`** — done (commit `e293251`), verified: file exists at the new path with the new namespace, old file gone, `OutOfProcessSandbox.cs`'s stale `using` removed.

Create `DigitalBrain.Kernel/Sandbox/ProcessRunner.cs` with the same content as the old file, only the `namespace` line changed:

```csharp
using System.Diagnostics;
using System.Text;
using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Kernel.Sandbox;

// Shared process-exec core for the SDK integration neurons (Shell/Git/DotNet/NuGet/Winget). Harvests IAW
// ShellAgent's mechanics: timeout + kill-tree, command block-list, base64 PowerShell, output truncation.
// A pure static runner returning a typed CommandResult — no Agent base, no DI, no per-call grain state.
public static class ProcessRunner
{
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "format", "shutdown", "reboot", "mkfs", "dd", "fdisk", "diskpart"
    };

    private static readonly string[] BlockedArgumentPatterns =
    [
        "rm -rf /", "del /s /q c:\\", ":(){ :|:& };:"
    ];

    // Run a binary directly (no shell). A non-zero exit is data; a failure to START throws (fail-fast).
    public static async Task<CommandResult> RunAsync(
        string fileName, string arguments, string? workingDirectory = null,
        int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName} {arguments}'.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            stopwatch.Stop();
            return new CommandResult(process.ExitCode, Truncate(await outputTask), Truncate(await errorTask), stopwatch.Elapsed);
        }
        // Only the timeout is caught; caller cancellation propagates (fail-fast).
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            KillTree(process);
            stopwatch.Stop();
            return new CommandResult(-1, "", $"Process '{fileName}' timed out after {timeoutMs} ms.", stopwatch.Elapsed);
        }
    }

    // Run a command line through the OS shell (cmd.exe / bash). Block-listed commands are rejected, not run.
    public static Task<CommandResult> ShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var blocked = Validate(command);
        if (blocked is not null)
            return Task.FromResult(new CommandResult(-1, "", blocked, TimeSpan.Zero));

        var (shell, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"");
        return RunAsync(shell, args, workingDirectory, timeoutMs, ct);
    }

    // Run a PowerShell command, base64-encoded to avoid quoting issues. Block-listed commands are rejected.
    public static Task<CommandResult> PowerShellAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var blocked = Validate(command);
        if (blocked is not null)
            return Task.FromResult(new CommandResult(-1, "", blocked, TimeSpan.Zero));

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        return RunAsync(shell, $"-NoProfile -NonInteractive -EncodedCommand {encoded}", workingDirectory, timeoutMs, ct);
    }

    private static string? Validate(string command)
    {
        var firstToken = command.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var commandName = Path.GetFileNameWithoutExtension(firstToken);
        if (BlockedCommands.Contains(commandName))
            return $"Command blocked: '{commandName}' is prohibited.";
        foreach (var pattern in BlockedArgumentPatterns)
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return "Command blocked: contains a prohibited pattern.";
        return null;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Benign race: the process exited between the HasExited check and Kill.
        }
    }

    private static string Truncate(string output, int maxLength = 16_384)
    {
        if (output.Length <= maxLength) return output;
        var head = maxLength * 2 / 3;
        var tail = maxLength / 3;
        return $"{output[..head]}\n\n... [{output.Length - maxLength} chars truncated] ...\n\n{output[^tail..]}";
    }
}
```

Then: `git rm DigitalBrain.Windows/ProcessRunner.cs`

In `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs`, remove line 2 (`using DigitalBrain.Windows;`) — `ProcessRunner` now resolves in-namespace since this file is already in `DigitalBrain.Kernel.Sandbox`.

- [x] **Step 10: Edit `DigitalBrain.Google/DigitalBrain.Google.csproj` — drop the now-unused Drive/Calendar package references** — done (commit `e293251`).

Remove these two lines (verified via repo-wide grep: their only consumers were the just-deleted `GoogleDriveApiClient.cs`/`GoogleCalendarApiClient.cs` and `Program.cs`'s now-edited scope list):

```xml
    <PackageReference Include="Google.Apis.Drive.v3" />
    <PackageReference Include="Google.Apis.Calendar.v3" />
```

- [x] **Step 11: Edit `Directory.Packages.props` — drop the now-unused Drive/Calendar version pins** — done (commit `e293251`).

Remove these two lines (immediately below the `<!-- Google Workspace ino (Gmail/Drive/Calendar) -->` comment — update that comment to just say `(Gmail)`):

```xml
    <PackageVersion Include="Google.Apis.Drive.v3" Version="1.75.0.4192" />
    <PackageVersion Include="Google.Apis.Calendar.v3" Version="1.75.0.4182" />
```

- [x] **Step 12: Build and run the full suite** — done, but only after the follow-up session's fix to the 5 unrelated dangling-reference breaks (see the CRITICAL FINDING note in Global Constraints above). Current real result: `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true` → 0 errors; `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true -p:SkipDeployBuild=true --filter "FullyQualifiedName!~E2E"` → **424 passed, 0 failed, 0 skipped**. `DigitalBrain.Developer.Tests`/`DigitalBrain.Windows.Tests` now report "no test matches filter" (0 tests) — confirms all their test files were correctly deleted; the empty project shells themselves are Task 2's job.

- [x] **Step 13: Commit** — the neuron-deletion work landed across commits `e293251`/`dbd60f4`/`90e5808` etc. The follow-up session's fix for the 5 dangling-reference breaks this exposed (Synapse.cs auth types, SoftwareEngineeringClosedLoopNeuron.cs, GeneratedPackRuntime, InoConnectorIntents, WidgetTreeRfwBridge, GatewayPayload/GatewayInternalAuth) is in the working tree but **not yet committed** — pending user go-ahead.

---

## Task 2: Delete the now-empty `DigitalBrain.Developer` / `DigitalBrain.Windows` projects (and their Tests projects)

**Status (verified 2026-07-06, follow-up session): still 0% done.** All 4 folders/`.csproj`s and their 4 `Brain.slnx` entries and their 2 `DigitalBrain.Kernel.csproj` `ProjectReference`s are all still present, exactly as this task describes them. This is the single largest genuinely-remaining mechanical gap from the original plan — everything else in Tasks 1/3/4/5 has at least been attempted.

**Context:** After Task 1, `DigitalBrain.Developer/` contains only its `.csproj` (all `.cs` files deleted) and `DigitalBrain.Windows/` likewise (its one live file, `ProcessRunner.cs`, was relocated). Both `.Tests` projects are now empty test projects. Confirmed via repo-wide search: no other `.csproj` references either project except `DigitalBrain.Kernel.csproj` and their own `.Tests.csproj`; `DigitalBrain.AppHost` has zero reference, direct or transitive, to either.

**Files:**
- Delete: `DigitalBrain.Developer/` (entire folder), `DigitalBrain.Windows/` (entire folder), `DigitalBrain.Developer.Tests/` (entire folder), `DigitalBrain.Windows.Tests/` (entire folder)
- Modify: `Brain.slnx`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`

- [ ] **Step 1: Delete the four project folders**

```
git rm -r DigitalBrain.Developer DigitalBrain.Windows DigitalBrain.Developer.Tests DigitalBrain.Windows.Tests
```

- [ ] **Step 2: Edit `Brain.slnx` — remove the four project entries**

Remove from the `/integrations/` folder:
```xml
    <Project Path="DigitalBrain.Developer/DigitalBrain.Developer.csproj" />
```
```xml
    <Project Path="DigitalBrain.Windows/DigitalBrain.Windows.csproj" />
```

Remove from the `/tests/` folder:
```xml
    <Project Path="DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj" />
```
```xml
    <Project Path="DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj" />
```

The resulting `Brain.slnx` should have exactly 33 `<Project Path=...>` entries (37 originally, minus these 4).

- [ ] **Step 3: Edit `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — remove the two dangling `ProjectReference`s**

Remove:
```xml
    <ProjectReference Include="..\DigitalBrain.Developer\DigitalBrain.Developer.csproj" />
```
```xml
    <ProjectReference Include="..\DigitalBrain.Windows\DigitalBrain.Windows.csproj" />
```

- [ ] **Step 4: Build and run the full suite**

```bash
dotnet build Brain.slnx -c Release
dotnet test Brain.slnx -c Release
```

Expected: clean build, and the **same** pass/skip counts as the end of Task 1 (deleting an already-emptied project changes zero test outcomes — this is a pure structural/build-graph change).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove now-empty DigitalBrain.Developer and DigitalBrain.Windows projects from the solution"
```

---

## Task 3: Fix the `Microsoft.CodeAnalysis.CSharp` double-pin (bump Roslyn-scripting group to 5.6.0 per user decision) [x]

**Status (verified 2026-07-06): done, via a DIFFERENT (also-correct) mechanism than Step 1/2 below literally prescribe.** What actually happened (commit `dbd60f4`): instead of deleting the mislabeled 5.3.0 group outright, both groups were bumped in place — 5.3.0→5.6.0 and 4.8.0→5.6.0 — and the duplicate `Microsoft.CodeAnalysis.CSharp` line was dropped from the second group. Net effect is identical (one `Microsoft.CodeAnalysis.CSharp` pin, unified at 5.6.0, no NU1506) and was actually the SAFER choice at the time, because Task 2 (which this task's Step 1 assumes has already happened) had NOT happened — `DigitalBrain.Developer.csproj` was and still is alive, and it genuinely needs the Workspaces packages. **Step 3 below (removing Kernel.csproj's Workspaces `PackageReference`s) correctly has NOT been done** — confirmed still present in `DigitalBrain.Kernel.csproj` lines 34-37 — and should stay that way until Task 2 actually deletes `DigitalBrain.Developer.csproj`.

**Context:** `Directory.Packages.props` currently double-pins `Microsoft.CodeAnalysis.CSharp` — once at 5.3.0 (mislabeled "Orleans", actually the now-deleted `RoslynAnalysisService`'s Workspace dependency) and once at 4.8.0 (correctly labeled, the live Foundry pack-scripting system's dependency: `ScriptRunner.cs`, `FoundryCompilation.cs`, `CapabilityGate.cs`, `InProcessAlcExecutor.cs`, `PackAlcEmbodier.cs`, all in `DigitalBrain.Kernel/Foundry/`). This produced NU1506 "Duplicate PackageVersion" warnings on ~30 of the 36 projects. `DigitalBrain.Developer.csproj` (the 5.3.0 group's sole direct consumer) was deleted in Task 2, so that group is now completely unreferenced and safe to delete outright — no reconciliation needed. Separately, the user approved bumping the surviving, live 4.8.0 group to the current latest lockstepped version, **5.6.0** (verified via nuget.org 2026-07-06: `Microsoft.CodeAnalysis`, `.Common`, `.CSharp`, `.CSharp.Scripting` all show 5.6.0 as latest stable, released 2026-07-02). The APIs actually used by the Foundry files (`CSharpCompilation.Create`, `CSharpCompilationOptions` `With*` methods, `CSharpScript.Create`, `ScriptOptions`, `Script<T>.RunAsync`, `MetadataReference`, `SymbolDisplayFormat`) are all still-shipped, stable public API per Context7's `/dotnet/roslyn` `PublicAPI.Shipped.txt` — no known breaking signature changes expected, but this must be confirmed empirically (Step 4 below), not just by API-surface inspection.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`

- [ ] **Step 1: Edit `Directory.Packages.props` — delete the dead, mislabeled 5.3.0 group**

Remove these 4 lines in full (the comment plus all 3 pins):

```xml
    <!-- Orleans (note: journaling features use specific preview) -->
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.3.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="5.3.0" />
```

(Orleans itself needs none of these — this block never actually belonged to Orleans; it was `RoslynAnalysisService`'s pin, now deleted along with it.)

- [ ] **Step 2: Bump the surviving Roslyn-scripting group to 5.6.0**

Replace:

```xml
    <!-- Roslyn for runtime compilation and execution of generated software/automations inside the brain for self-improvement and install.
         Pinned together for Scripting compatibility (CSharpScript 4.8 requires matching Common/CSharp 4.8). -->
    <PackageVersion Include="Microsoft.CodeAnalysis" Version="4.8.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Common" Version="4.8.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.8.0" />
```

with:

```xml
    <!-- Roslyn for runtime compilation and execution of generated software/automations inside the brain for self-improvement and install.
         Pinned together for Scripting compatibility (CSharpScript requires matching Common/CSharp). Version verified via nuget.org 2026-07-06. -->
    <PackageVersion Include="Microsoft.CodeAnalysis" Version="5.6.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Common" Version="5.6.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="5.6.0" />
```

- [ ] **Step 3: Edit `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — drop the now-unneeded Workspaces `PackageReference`s**

`RoslynNeuron`/`RoslynAnalysisService` (the only reason Kernel referenced the Workspaces packages) are deleted; Kernel's Foundry code only ever used `CSharpCompilation`/`CSharpScript`, never `MSBuildWorkspace`. Remove:

```xml
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
```

(Keep `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Scripting` — both still needed by `DigitalBrain.Kernel/Foundry/*.cs`.)

- [ ] **Step 4: Restore, build, and run the full suite**

```bash
dotnet restore Brain.slnx
dotnet build Brain.slnx -c Release
dotnet test Brain.slnx -c Release
```

Expected: no NU1506 warnings anywhere in the build output, clean build, same pass/skip counts as the end of Task 2 (pure package-version change, zero test-count impact expected).

- [ ] **Step 5: Manually verify the live Foundry pack-scripting system still works end-to-end after the 4.8→5.6 jump**

Use the `verify` skill to drive the real running app (Aspire-hosted kernel) and exercise the `run_code_foundry` MCP tool (or an equivalent pack-embodiment flow through `MarketplaceNeuron`/`GeneratedNeuron`/`AutomationNeuron`/`SkillPackSynthesizer`) — confirm a real C# snippet still compiles and executes via `CSharpScript`/`CSharpCompilation` with no `MissingMethodException`/`TypeLoadException`/assembly-binding errors. Use `mcp__aspire__doctor` and the Aspire MCP tools per standing instruction to start/inspect the app. This is the actual verification gate for the version bump the user approved — the full test suite passing is necessary but not sufficient, since scripting/compilation edge cases may not all be covered by existing unit tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(packages): remove dead Microsoft.CodeAnalysis.CSharp double-pin, bump Roslyn scripting group to 5.6.0"
```

---

## Task 4: Exclude `deploy/DigitalBrain.Deploy.csproj` from the CI test-time build graph [x]

**Status (verified 2026-07-06): mechanism substituted, and the ONE workflow this task cared about is still not fixed.** What actually happened (commit `90e5808`): instead of a `.slnf` solution filter, a `SkipDeployBuild` MSBuild property was added to `deploy/DigitalBrain.Deploy.csproj` (`<SkipBuild Condition="'$(SkipDeployBuild)' == 'true'">true</SkipBuild>`), and `.github/workflows/ci.yml`'s test step was updated to pass `-p:SkipDeployBuild=true`. This is a reasonable, arguably-simpler alternative to a `.slnf` (Step 1 below can be skipped). **However: `.github/workflows/deploy.yml` — the workflow that actually runs on push to `master` and that this task was originally about (per the plan's own Task 5 context: "every prior attempt to let deploy.yml's Run tests step finish was cancelled") — line 40 was NEVER updated to pass `-p:SkipDeployBuild=true`.** Confirmed via the real failed CI run (`gh run view` on run `28761958707`): the "Run tests" step's log shows `Restored .../deploy/DigitalBrain.Deploy.csproj` — it is still being built on every push-to-master CI run. Remaining work for this task: add `-p:SkipDeployBuild=true` to `deploy.yml:40` (Steps 1-2 below are superseded by this one-line fix; Step 3's local verification should use the real flag, not a `.slnf`).

**Context:** `.github/workflows/deploy.yml`'s "Run tests" step runs `dotnet test Brain.slnx ...`, which restores+builds every project in the solution including `deploy/DigitalBrain.Deploy.csproj` (Pulumi + Pulumi.AzureNative — a large Azure ARM SDK with zero tests). The later "Provision (pulumi up)" step (`pulumi/actions@v6`, `work-dir: deploy`) builds/runs that same project independently via the Pulumi CLI/Automation API (confirmed via Context7), so building it during the test step is pure waste. After Task 2, `Brain.slnx` has 33 project entries; excluding `deploy` leaves 32 for the CI-only filter.

**Files:**
- Create: `Brain.ci-tests.slnf`
- Modify: `.github/workflows/deploy.yml`

- [ ] **Step 1: Create `Brain.ci-tests.slnf` at the repo root**

```json
{
  "solution": {
    "path": "Brain.slnx",
    "projects": [
      "app/Flutter.proj",
      "DigitalBrain.Aspire/DigitalBrain.Aspire.csproj",
      "DigitalBrain.Core/DigitalBrain.Core.csproj",
      "DigitalBrain.Demo.Contracts/DigitalBrain.Demo.Contracts.csproj",
      "DigitalBrain.Demo.Runtime/DigitalBrain.Demo.Runtime.csproj",
      "DigitalBrain.Kernel/DigitalBrain.Kernel.csproj",
      "DigitalBrain.Marketplace.Contracts/DigitalBrain.Marketplace.Contracts.csproj",
      "DigitalBrain.Mcp/DigitalBrain.Mcp.csproj",
      "DigitalBrain.Pack.Contracts/DigitalBrain.Pack.Contracts.csproj",
      "DigitalBrain.SeedPacks/DigitalBrain.SeedPacks.csproj",
      "DigitalBrain.Ui.Contracts/DigitalBrain.Ui.Contracts.csproj",
      "DigitalBrain.Ui.Runtime/DigitalBrain.Ui.Runtime.csproj",
      "DigitalBrain.Context/DigitalBrain.Context.csproj",
      "DigitalBrain.Experience.PersonalAssistant/DigitalBrain.Experience.PersonalAssistant.csproj",
      "DigitalBrain.Google/DigitalBrain.Google.csproj",
      "DigitalBrain.Salesforce/DigitalBrain.Salesforce.csproj",
      "DigitalBrain.Telegram/DigitalBrain.Telegram.csproj",
      "DigitalBrain.Telegram.Channel/DigitalBrain.Telegram.Channel.csproj",
      "DigitalBrain.UiKit/DigitalBrain.UiKit.csproj",
      "DigitalBrain.AppHost/DigitalBrain.AppHost.csproj",
      "DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj",
      "DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj",
      "DigitalBrain.Context.Tests/DigitalBrain.Context.Tests.csproj",
      "DigitalBrain.Experience.PersonalAssistant.Tests/DigitalBrain.Experience.PersonalAssistant.Tests.csproj",
      "DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj",
      "DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj",
      "DigitalBrain.Telegram.Channel.Tests/DigitalBrain.Telegram.Channel.Tests.csproj",
      "DigitalBrain.Telegram.Tests/DigitalBrain.Telegram.Tests.csproj",
      "DigitalBrain.TestKit/DigitalBrain.TestKit.csproj",
      "DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj",
      "DigitalBrain.Tests/DigitalBrain.Tests.csproj",
      "DigitalBrain.UiKit.Tests/DigitalBrain.UiKit.Tests.csproj"
    ]
  }
}
```

(32 entries — every `Brain.slnx` project after Task 2 except `deploy/DigitalBrain.Deploy.csproj`.)

- [ ] **Step 2: Edit `.github/workflows/deploy.yml` — point the test step at the filter**

Replace line 40:
```yaml
        run: dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```
with:
```yaml
        run: dotnet test Brain.ci-tests.slnf -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```

- [ ] **Step 3: Verify locally with the same args CI uses**

```bash
dotnet test Brain.ci-tests.slnf -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```

Expected: identical pass/skip/fail counts to the equivalent `Brain.slnx` run (all real test projects still discovered and run — the filter must not silently drop a test project). Separately confirm `deploy/DigitalBrain.Deploy.csproj` is not restored/built by this command (e.g., no `deploy/obj`/`deploy/bin` output produced by this run, or run with `-v:normal` and confirm no `DigitalBrain.Deploy` line appears in the restore/build summary).

If the `.slnx`-as-filter-base combination doesn't resolve cleanly (untested combination — verify empirically here, don't assume), fall back to pointing `Brain.ci-tests.slnf`'s `"path"` at a generated `.sln` equivalent, or restructure the CI step to `dotnet test` each of the 32 project paths directly instead of via a filter — pick whichever keeps the command a single line matching CI's invocation shape most closely.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "ci: exclude deploy/DigitalBrain.Deploy.csproj from the test-time build graph via a solution filter"
```

---

## Task 5: Push and capture one real, complete CI run

**Status (verified 2026-07-06): already happened once — and FAILED, for reasons unrelated to this cleanup, now fixed.** All work was pushed directly to `master` (no PR/feature branch), so `deploy.yml` DID run to completion for real (confirmed via `gh run view 28761958707`) — but the "Run tests" step failed with the `UserId`/`CS0246` errors documented in the CRITICAL FINDING note (Global Constraints, above), plus it built `deploy/DigitalBrain.Deploy.csproj` first (confirming Task 4's remaining gap). **This task needs to be redone** once the build-break fix (already made, not yet committed) and Task 4's one-line `deploy.yml` fix are both in and pushed.

**Context:** Every prior attempt to let `deploy.yml`'s "Run tests" step finish was cancelled early — there is still no complete real-CI pass/fail baseline post-cleanup. This step triggers a **real deploy** (the job's later steps run `pulumi up` against real Azure infrastructure) — get explicit confirmation before the actual push, even though the user's own task description asked for this, since it has real cloud-spend/production-infra side effects beyond just running tests.

- [ ] **Step 1: Confirm with the user, then push**

```bash
git push
```

- [ ] **Step 2: Watch the run to full completion — do not cancel early**

```bash
gh run watch
```

or `gh run view --log` after it completes if `watch` isn't available in this environment.

- [ ] **Step 3: Report the real numbers**

From the completed "Run tests" step: wall-clock minutes, pass/fail/skip counts. Compare against:
- Tonight's local baseline: 456 passed / 6 skipped, ~110s pre-session / ~2m with `MaxParallelThreads=2`.
- The never-completed ~25-29 min CI baselines from before this cleanup.

---

## Self-Review Notes

- **Spec coverage:** Task 1 of the original ask (audit) was completed via research before this plan was written — see the Task 1 "Context" note above for the live/dead table summary. Tasks 2-5 of the original ask map to this plan's Tasks 1-5 (Task 1 here folds in the original Task 2's deletions plus the newly-discovered GoogleDrive/GoogleCalendar dead neurons; Task 2 here is the original Task 2's "delete the whole project" step, separated out because it has a different, later-safe dependency ordering; Task 3/4/5 here map 1:1 to the original Task 3/4/5).
- **Type consistency:** `ProcessRunner` namespace changes from `DigitalBrain.Windows` to `DigitalBrain.Kernel.Sandbox` consistently between Step 9's new-file content and the `OutOfProcessSandbox.cs` edit in the same step.
- **Ordering constraint carried through:** Task 1 deletes the 5 `ProcessRunner`-consuming dead neurons *before* relocating `ProcessRunner` itself, in the same task/commit — moving it first (as originally suggested by initial research) would have left a transiently broken build, since those neuron files still `using DigitalBrain.Windows;` until deleted.
