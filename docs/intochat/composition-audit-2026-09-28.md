# IntoChat composition audit — 2026-09-28

IntoChat should be a composition host: DI, endpoint mapping, config. It is ~6,800 lines; ~900 are
composition. The rest is module code in the wrong place, fakes registered in production, or dead code.
Findings come from three read-only audits (grep + read). Nothing was changed.

## Real bugs

1. **Workspace deletion is a no-op.** `DELETE /workspaces/{id}` returns 202; `MemoryWorkspaceVectorPurge`
   purges namespace `intochat.workspace` that nothing writes, `HostedWorkspaceBackupPurge` enqueues into a
   queue nothing reads (`Operations/WorkspaceDeletion.cs:24,39-55`).
2. **Fakes in production.** `DeterministicBackgroundRemover` (`Program.cs:39`) charges Compute and returns
   nonexistent asset ids. `DeterministicWebResearchProvider` makes Lead Generator return invented companies
   (`LeadGeneratorLeadsNeuron.cs:56`).
3. **Problem reports in memory** (`Operations/ProblemReport.cs:19`), lost on restart.
4. **Invited members can't log in.** `AuthenticateAsync` uses `First(Role == Owner)` (`IdentityDirectoryNeuron.cs:66`).
5. **Hard-coded callers** bypass stamped identity: `AccountSession.DefaultLogin` (`BackgroundRemoval.cs:123`),
   `"assistant"`/`"default"` (`LeadGeneratorLeadsNeuron.cs:39-40`).

## Composition problems

- ~16 grains live in the host: Identity (2), Files/Images (~6), BuiltIn apps (2), LeadGenerator, AppDraft,
  ShellPersistence (2).
- Whole `IModule`s inside the host: `IdentityModule`, `LeadGeneratorModule`.
- Host hard-codes module tool names: `AgentEndpoints.cs:266,287` (`show_supabase_query_table`, `table_read`,
  `table_refine`); `AssistantDefinition.cs:9-11` prompt names Supabase/form/C# tools. Tool results are
  JSON-sniffed (`isError`, `_ui`, `rowsRead`, `title`) instead of typed.
- `/agent` handler (`AgentEndpoints.cs:35-211`) is ~200 lines of turn orchestration (runs, metering, receipts,
  AG-UI events) with empty catches and the logger rebuilt 4×.
- Duplication: window-open retry loop ×4 (`AppEndpoints:204`, `LeadGeneratorLeadsNeuron:76`,
  `WorkspaceFormTools:79`, `LiveTableWindows:44`); workspace access enforced 3 ways (`AccountSession:76-87`,
  `WorkspaceAccessFilter`, manual `CanAccessAsync`); id validation ×3; filter mapping ×2 with different null
  handling (`WorkspaceTableTools:77`, `WorkspaceTableAdapter:35`); C# activation guard ×2; dev-mode filter ×2.
- Provider special cases in `WorkspaceConnectionsEndpoints.cs` (`salesforce`, `gmail`, `github`).
- `IntoChat.csproj` suppresses CA1812 (hides never-instantiated classes) and references dev-only modules
  unconditionally.
- `LocalFiles/` is Azure blob storage; UI label says "Local filesystem".

## Dead code (~600 lines)

`Http/SseWriter.cs`, `Operations/BackupPlan.cs`, `Auth/SecretKeyMigration.cs` (one-off, runs every boot),
`LiveTableWindows.afterTableCreated`, `AgentReceipts.ShadowPrice(IntentContext)` (test-only),
`BackgroundRemovalOptions.FailingImages`, `WebResearch.BrowseAsync`, `CreateAccountAsync`,
`EnsureOwnerAsync` (test-only), `BasicCredential.FromConfiguration`, `IdentityEndpoints.DefaultWorkspace`,
`HostedWorkspaceBackupPurge.Pending`, `ProblemReportStore.ListAsync`, `ImageSaveCoordinator.previousOperation`,
signals `BuiltInAppChanged` and `LeadGeneratorSwept`, test-only endpoints `/leadgenerator/run` and
`/background-removal/*`, grants/members/invitations routes with no Flutter caller.

## Where things belong

| Host code | Target | ~Lines |
|---|---|---|
| `Identity/**`, `Auth/AccountSession.cs` | new `Modules/Identity` | 850 |
| `Auth/BlobProtectionKeys.cs` | `Kernel/Sdk/Secrets` | 80 |
| Apps image files, `LocalFiles/**`, BackgroundRemoval, WorkspaceAssets | new Images/Files module | 1,050 |
| `Marketplace/**`, `Packages/**`, AppDraft | `DigitalBrain.Apps` | 750 |
| `CSharp/**` | `Microsoft/CSharp` | 210 |
| `LiveTableWindows`, `WorkspaceTableTools`, `WorkspaceTableAdapter` | Supabase | 220 |
| `Workspace/ShellPersistence.cs` | Flutter.Workspace | 330 |
| Agent turn loop, receipts, model catalog, voice, discovery tools | `AI/Agents`, Compute, Discovery | 800 |
| LeadGenerator | own module on `AI/WebSearch`, or delete (owner decision) | 265 |
| BuiltIn Assistant/Settings apps | Apps (see assistant-as-first-app design) | 275 |

## Proposed order

1. Delete dead code, drop CA1812 suppression.
2. Fix the five bugs.
3. Typed tool results + module-contributed tools/prompts; move turn loop to `AI/Agents`.
4. Extract `Modules/Identity`, single access check.
5. Extract Images/Files module.
6. Move Marketplace/Packages/C#/live-table/ShellPersistence/LeadGenerator.

Target host: ~900 lines (`Program.cs`, AppHost, CORS, thin endpoint maps).
