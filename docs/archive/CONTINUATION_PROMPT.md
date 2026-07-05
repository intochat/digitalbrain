# Continuation Prompt

Continue the architecture cleanup in `E:\digitalbraintech\brain`.

Context:

- Brain.slnx is the only canonical solution. Do not recreate `Brain.CI.slnx` or `Brain.Full.slnx`.
- Latest cleanup commit from the prior cycle:
  - `f128306 Start Core boundary pack contracts`
- That commit started Phase 2 by adding `DigitalBrain.Pack.Contracts` and moving executable pack contracts out of `DigitalBrain.Core`:
  - `IPackBehavior`
  - `PackManifest`
  - `PackConfigField`
  - `PackEmission`
  - `ConfigurationProvided`
  - `ConfigFormSurface`
  - pack trust helpers
  - `KitExperience` / `UiExperience`
- `DigitalBrain.Core` now has guard tests in `DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs` that prevent Core from referencing other `DigitalBrain.*` assemblies, runtime/host/integration packages, or `DigitalBrain.Pack.Contracts`.
- Validation already passed after `f128306`:
  - `dotnet build Brain.slnx -v quiet -p:SkipFlutterBuild=true`
  - `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E" -v minimal`
- The prior run had to stop stale local `DigitalBrain.Kernel.exe` processes because they locked build outputs.

Important dirty-worktree note:

The prior cleanup commit intentionally left unrelated local edits unstaged. Before starting a new cleanup slice, inspect and preserve or explicitly route around:

- `DigitalBrain.Kernel/Gateway/GatewayService.cs`
- `DigitalBrain.Kernel/Ino/InoNeuron.cs`
- `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs`
- `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
- untracked `DigitalBrain.Tests/Ino/`

Do not revert those unless explicitly requested.

Recommended next cleanup slice:

Start with the lowest-risk continuation of Phase 2: reduce Core's remaining UI/marketplace projection coupling.

Preferred scope:

1. Read `ARCHITECTURE_CLEANUP_PROPOSAL.md` first.
2. Inspect `DigitalBrain.Core/UiSurfaces.cs`, especially marketplace list/live-data builders and any `NeuroPack` projection helpers.
3. Identify a small extraction candidate that unblocks moving more pack/marketplace contracts later without pulling Kernel or integration dependencies into Core.
4. Prefer one of these small moves:
   - move marketplace UI projection helpers out of Core into a clearer package, or
   - introduce `DigitalBrain.Ui.Contracts` for UI schema only, if dependency direction stays clean.
5. Preserve source compatibility where practical.
6. Add or extend architecture guard tests so Core does not regain runtime/host/integration dependencies.
7. Update `Brain.slnx`, README/docs, and project references.
8. Run:
   - `dotnet build Brain.slnx -v quiet -p:SkipFlutterBuild=true`
   - `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E" -v minimal`
9. Commit the result.

Be careful:

- Do not put concrete marketplace seed content back into `DigitalBrain.Core`.
- Preserve Orleans technical "silo" terms; only rename product/runtime terms.
- Do not start a giant Kernel split until the Core guard/extraction slice is finished and validated.
- If touching Aspire/AppHost behavior, use the repo-local Aspire skill under `.agents/skills/aspire`.
