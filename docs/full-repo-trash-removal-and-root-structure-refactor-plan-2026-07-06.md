# Full Repo Trash Removal, Cleanup & Root Structure Refactor Plan (2026-07-06)

**Status:** Proposed plan (post Ino integration commit)  
**Based on:** `architecture-trash-analysis-2026-07-06.md`, `CLEANUP_PROPOSAL.md`, `ARCHITECTURE_CLEANUP_PROPOSAL.md`, recent Ino-as-integration work.  
**Commit note:** The Ino pluggable integration + AI config extraction (`DigitalBrain.Ino/`, registration, classifier move) has been committed (amended commit on master).

## 1. Problems with Current State
- **Root directory tree is terrible** (flat clutter):
  - ~25+ top-level directories directly in `E:\brain\` (ls view / explorer).
  - Dozens of `DigitalBrain.*` projects mixed with `app/`, `deploy/`, `demo/`, `.agents/`, `docs/`, etc.
  - Makes navigation, onboarding, and "what is core vs integration" confusing.
  - slnx uses logical `<Folder>`s (`/src/`, `/integrations/`, `/hosts/`, `/tests/`) but physical layout does not match.

- **Trash / low-value projects** (examples and candidates):
  - `DigitalBrain.Experience.PersonalAssistant/` + `.Tests/` : Pure `IPackBehavior` demo pack (Telegram + Context + LLM via Signals). Source is embedded as marketplace seed in `SeedPacks`. 
    - Now that we have a proper `DigitalBrain.Ino/` integration (with `InoAiOptions` + AI config ownership + classifier), this looks like legacy "useless shie".
    - In older analysis it was "keep for grain-vs-pack separation", but Ino grain + new integration + packs in general make the specific Experience.PersonalAssistant redundant.
  - Demo projects (`DigitalBrain.Demo.Contracts/`, `.Runtime/`) : Surface demo only; may be bloat if not core to self-evolution story.
  - Various heavy `.Tests` projects and spikes.
  - Potential dead code (SystemRollingSurfaces, PrototypeJournals in some contexts, remaining references to pruned items).
  - Root-level `.md` files (CLEANUP_PROPOSAL.md, ARCHITECTURE_CLEANUP_PROPOSAL.md) that belong in `docs/`.
  - Temp artifacts occasionally appearing (e.g. `*.metaproj.tmp`).

- **Inconsistent with "Ino as integration" direction**: We just made Ino first-class (like Google/Salesforce) with its own config. The old PersonalAssistant pack now feels like duplicate/legacy assistant concept.
- **Overall bloat** slows `ls`, searches, builds, reviews. Violates AGENTS.md "Delete aggressively", "Simplify".

## 2. Target Vision (Proper Structure + Minimal Trash)
Adopt + adapt the structure from `ARCHITECTURE_CLEANUP_PROPOSAL.md` (physical now matches logical slnx folders).

```
brain/
├── app/                          # clients (Flutter) - unchanged
├── deploy/                       # Pulumi etc. - unchanged
├── docs/                         # all .md, plans, specs (clean root!)
├── eng/                          # (optional) CI/scripts if any
├── src/                          # stable/core/runtime (the "product")
│   ├── DigitalBrain.Core/
│   ├── DigitalBrain.Kernel/
│   ├── DigitalBrain.Aspire/
│   ├── DigitalBrain.Mcp/
│   ├── DigitalBrain.Pack.Contracts/
│   ├── DigitalBrain.Marketplace.Contracts/
│   ├── DigitalBrain.Ui.Contracts/
│   ├── DigitalBrain.Ui.Runtime/
│   ├── DigitalBrain.SeedPacks/
│   ├── DigitalBrain.Demo.Contracts/   # (if kept)
│   └── DigitalBrain.Demo.Runtime/     # (if kept)
├── integrations/                 # pluggable capabilities (now includes Ino properly)
│   ├── DigitalBrain.Ino/             # AI assistant + config (our recent work)
│   ├── DigitalBrain.Google/
│   ├── DigitalBrain.Salesforce/
│   ├── DigitalBrain.Context/
│   ├── DigitalBrain.Telegram/
│   └── ... (future packs)
├── hosts/                        # deployables / thin hosts
│   ├── DigitalBrain.AppHost/
│   ├── DigitalBrain.ServiceDefaults/
│   └── DigitalBrain.Telegram.Transport/
├── tests/                        # all test projects (or split further by speed)
│   ├── DigitalBrain.Tests/
│   ├── DigitalBrain.*.Tests/
│   └── DigitalBrain.TestKit/
└── (root only)
    ├── AGENTS.md
    ├── README.md
    ├── LICENSE
    ├── Brain.slnx
    ├── Directory.Packages.props
    └── .github/ .gitignore etc.
```

**Benefits**:
- Root is clean and scannable.
- `integrations/` makes "plug Ino / Google etc. into kernel" obvious.
- Matches "Kernel as composition root".
- Easier to see self-evolution boundaries.
- Supports future pack-first evolution.

**PersonalAssistant decision**: **Delete** as trash.
- Remove projects + slnx entries + .Tests.
- Remove/update the embedded seed in `SeedPacks/MarketplaceSeeds.cs` (either delete the demo pack or replace with a trivial inline string or one from the new Ino integration).
- Update all docs references.
- Rationale: superseded; grain Ino + integration now provides the real assistant story. Pack demos can live in docs/examples or Ino itself if needed.

## 3. AGENTS.md 5-Step Approach Applied
1. **Make requirements less dumb**: Question "must keep every demo pack". PersonalAssistant was useful once; now duplicate. Flat root was "convenient" but hurts long-term.
2. **Delete**: PersonalAssistant (and similar), root .md clutter, any proven-dead after searches. Delete >> add.
3. **Simplify**: Group by role (src vs integrations). One canonical layout.
4. **Accelerate**: Small phases. Fast `dotnet build && dotnet test` after every group of moves/deletes (plain commands from root; filters and -p:Skip* no longer needed by default). Use slnx folders as we go.
5. **Automate**: Scripted ref updates if possible; rely on build to catch broken ProjectReferences.

## 4. Phased Execution Plan (Delete-Heavy, Verifiable)
**Rule**: After every logical chunk:
- `dotnet build`
- `dotnet test` (plain, no filter)
- `aspire doctor` (MCP)
- `git status --ignored --short` to confirm no new junk.

### Phase 0: Commit + Baseline (Done)
- Ino integration committed.
- Run full baseline above.

### Phase 1 & 2 Progress (Completed in recent slices)
- integrations/ and hosts/ grouping completed in prior commits (Context, Google, Ino, Salesforce, Telegram; AppHost, ServiceDefaults, Telegram.Transport).
- src/ and tests/ physical moves completed (Core, Kernel, Aspire, Mcp, all *Contracts, SeedPacks, Ui.*, Demo.*, all *Tests + TestKit + DigitalBrain.Tests).
- All relative ProjectReferences and Brain.slnx updated.
- Legacy namespace pollution cleaned up (Ui.Contracts, Pack.Contracts, etc. now use correct namespaces; added required usings across Kernel, tests, etc.).
- Build now succeeds, non-cluster tests green, aspire doctor passes.
- Namespace correction was a larger effort than anticipated due to previous "everything under DigitalBrain.Core" pattern.

### Phase 1: Quick Deletes (Safest, Highest Signal)
- Delete `DigitalBrain.Experience.PersonalAssistant/` + `.Tests/`
  - Update `Brain.slnx`
  - Update `DigitalBrain.SeedPacks/MarketplaceSeeds.cs` (remove the pack or inline minimal)
  - Update docs (SYSTEM_DESIGN.md, plans, trash-analysis notes)
  - Remove from any test steps / seeds lists.
- Move root clutter .md to `docs/archive/` or `docs/`:
  - `CLEANUP_PROPOSAL.md`, `ARCHITECTURE_CLEANUP_PROPOSAL.md`
- Delete obvious temps if tracked.
- Verify: build + tests mentioning PersonalAssistant/Demo seeds.

### Phase 2: Root Tree Physical Restructure (Big but Worth It)
Adopt target above.

Sub-phases (one group at a time):
1. Create dirs (`src/`, `integrations/`, `hosts/`, `tests/`) at root.
2. Move stable/core projects into `src/` (Core, Kernel, Aspire, Mcp, contracts, SeedPacks, Demo.* if kept).
   - `git mv`
   - Fix all relative `<ProjectReference>` paths (search for `Include="..` patterns across *.csproj).
   - Update `Brain.slnx` `Path=` attributes.
3. Move integrations (Google, Salesforce, Context, Telegram, **Ino**, etc.) into `integrations/`.
4. Move hosts (AppHost, ServiceDefaults, Telegram.Transport) into `hosts/`.
5. Move test projects into `tests/`.
6. Clean remaining root (keep only essentials + .github etc.).

**Risk mitigation**:
- Do in isolated worktree or branch.
- Update one group + build immediately.
- Also fix any hard-coded paths in workflows, docs, test settings, Pulumi, etc.
- After moves: `dotnet restore`, full build, targeted tests.

### Phase 3: Deeper Trash + Polish (Next)
- Audit remaining using the trash-analysis table + `rg` for dead symbols.
- Decide on Demo.* (keep or move to samples?).
- Further splits inside Ino/Kernel if needed (per previous self-evo plan).
- Clean `Directory.Packages.props` (latest versions).
- Update architecture docs to reflect new tree.
- Remove any remaining "silo" naming, prototype journals where safe.
- Root polish: move clutter .md to docs/, clean .claude/ if local-only, add STRUCTURE.md.

### Phase 4: Verification & Automation
- Full non-E2E test suite.
- `aspire run` smoke (or targeted resources via MCP).
- Architecture guard tests (CoreBoundaryTests etc.) must pass.
- Add a root `README` or `STRUCTURE.md` describing the new layout.
- Consider adding a `build` script or just rely on `dotnet build Brain.slnx`.

## 5. Risks & Rollback
- Broken references during moves → git + build will catch early.
- Removing PersonalAssistant pack seed → marketplace demo loses one example. Mitigate by documenting "use Ino integration instead" or provide a tiny replacement pack.
- Big restructure blast radius → phase it, verify each sub-phase.
- Rollback: git revert / branch.

## 6. Done Criteria
- Root `ls` shows only ~8-10 essential items (app, deploy, docs, src, integrations, hosts, tests, .github, key files).
- No `DigitalBrain.Experience.PersonalAssistant` (or justified if kept).
- All Ino logic + config properly in `integrations/DigitalBrain.Ino/`.
- `dotnet build` + `dotnet test` (plain from root) green.
- Docs reflect reality.
- Follows AGENTS.md (net deletions, fast loop preserved).

## 7. Next Actions (Updated)
- Phase 1-2 (grouping + namespace cleanup): **COMPLETE** (verified build + tests + doctor green).
- Slice 3 (Trash): Target Demo.* and any obvious low-value items.
- Polish: Root cleanup, STRUCTURE.md, doc updates.
- Ino/self-evolution boundary audit.
- Final full verification.

Progress tracked in commits. Follow AGENTS.md fast loop on every change.

This aligns the entire repo with the "self-evolving OS where even Ino is a pluggable integration with its own AI config".

References: previous trash analysis, Ino refactor, AGENTS.md "Delete" step.

Ready to execute once approved.
