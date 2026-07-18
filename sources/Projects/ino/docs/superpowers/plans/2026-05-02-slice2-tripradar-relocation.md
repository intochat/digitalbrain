# Slice 2 — Tripradar Relocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `D:\ino\tripradar\` to `D:\ino\domains\travel\tripradar\` and merge all of its projects into `ino.slnx` so there is one solution to rule them all. Tripradar's Aspire AppHost stays separate (you still run it with its own `dotnet run --project ...`).

**Architecture:** Pure structural change — zero new features. `git mv` the folder, audit relative paths inside tripradar's source for repo-root traversal, update `ino.slnx` with new project paths, delete `tripradar/TripRadar.slnx`, update both repo CLAUDE.md files to reflect new paths.

**Tech Stack:** Git, .NET SDK (build verification), Aspire CLI (boot verification).

**Spec reference:** `docs/superpowers/specs/2026-05-02-phase4-epilogue-design.md` § Slice 2.

**Spec correction.** The spec says "all 25 of its projects" — the actual count from `tripradar/TripRadar.slnx` is **19 projects**. Plan uses the correct count.

---

## File structure

| File | Action | Responsibility |
|---|---|---|
| `tripradar/` (entire folder) | Move via `git mv` | Becomes `domains/travel/tripradar/` |
| `tripradar/TripRadar.slnx` | Delete (post-move) | Replaced by entries in `ino.slnx` |
| `ino.slnx` | Modify | Add 19 project entries under new `/domains/travel/tripradar/` folder |
| `tripradar/src/Aspire/AppHost.cs` | Audit + maybe modify | Verify no path climbs to old repo-root location |
| `tripradar/src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs` | Audit + maybe modify | Same |
| `D:\ino\CLAUDE.md` | Modify | Search-and-replace `tripradar/` → `domains/travel/tripradar/` |
| `tripradar/CLAUDE.md` | Modify | Update internal references to `TripRadar.slnx` (now removed) and any other absolute-ish paths |

---

## Task 1 — Pre-move snapshot

- [ ] **Step 1: Capture the 19 project paths from `tripradar/TripRadar.slnx`**

```
type D:\ino\tripradar\TripRadar.slnx
```
(or `cat tripradar/TripRadar.slnx` on Bash)

Expected: a `<Solution>` block with 19 `<Project Path="src/...">` entries. Save the list — you'll re-add them to `ino.slnx` with rewritten paths after the move.

- [ ] **Step 2: Verify clean working tree**

```
git status --short
```
If there are uncommitted changes (other than the master-branch state from prior work), bail and confirm with the user. The relocation is destructive enough that we want a clean baseline.

- [ ] **Step 3: Confirm tripradar currently builds clean (sanity baseline)**

```
dotnet build D:\ino\tripradar\TripRadar.slnx
```
Expected: clean build (warnings OK, no errors). If this is already broken, fix or skip with explicit user approval before continuing — otherwise you can't tell whether post-move failures are from the move or pre-existing.

---

## Task 2 — Move the folder

- [ ] **Step 1: Run `git mv` from the repo root**

```
cd D:\ino
git mv tripradar domains/travel/tripradar
```
Expected: git stages the rename. `git status` should show ~50–200 renames (every file in tripradar) under the new path with no content changes.

- [ ] **Step 2: Verify the move**

```
git status --short | head -20
```
Expected: lines like `R  tripradar/CLAUDE.md -> domains/travel/tripradar/CLAUDE.md`.

```
ls D:\ino\tripradar
```
Expected: error / does not exist.

```
ls D:\ino\domains\travel\tripradar
```
Expected: lists tripradar's contents (CLAUDE.md, README.md, src/, .editorconfig, etc.).

---

## Task 3 — Update `ino.slnx`

**Files:**
- Modify: `D:\ino\ino.slnx`

- [ ] **Step 1: Read the current `ino.slnx`**

```
Read D:\ino\ino.slnx
```
Note the existing folder structure — `/domains/travel/` already has 3 projects. We add a `/domains/travel/tripradar/` subfolder with 19 entries.

- [ ] **Step 2: Insert the new folder block**

Find the existing `/domains/travel/` folder block in `ino.slnx`:

```xml
<Folder Name="/domains/travel/">
  <Project Path="domains/travel/Ino.Domains.Travel.Contracts/Ino.Domains.Travel.Contracts.csproj" />
  <Project Path="domains/travel/Ino.Domains.Travel/Ino.Domains.Travel.csproj" />
  <Project Path="domains/travel/Ino.Domains.Travel.Tests/Ino.Domains.Travel.Tests.csproj" />
</Folder>
```

Add a new sibling folder immediately after it:

```xml
<Folder Name="/domains/travel/tripradar/">
  <Project Path="domains/travel/tripradar/src/Aspire/Aspire.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Bot/TripRadar.Bot.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Bot.Tests/TripRadar.Bot.Tests.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Aspire.Tests/TripRadar.Aspire.Tests.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.DeploymentKit/TripRadar.DeploymentKit.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.MiniApp.Infrastructure/TripRadar.MiniApp.Infrastructure.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.MiniApp/TripRadar.MiniApp.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.ServiceDefaults/TripRadar.ServiceDefaults.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.API/TripRadar.Server.API.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.API.Contracts/TripRadar.Server.API.Contracts.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Application/TripRadar.Server.Application.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Comms.Core/TripRadar.Server.Comms.Core.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Db/TripRadar.Server.Db.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Infrastructure/TripRadar.Infrastructure.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Domain/TripRadar.Server.Domain.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Infrastructure/TripRadar.Server.Infrastructure.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Jobs.API/TripRadar.Server.Jobs.API.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Mocks/TripRadar.Server.Mocks.csproj" />
  <Project Path="domains/travel/tripradar/src/TripRadar.Server.Tests/TripRadar.Server.Tests.csproj" />
</Folder>
```

If `tripradar/TripRadar.slnx` (Task 1 Step 1) has DIFFERENT projects than this list — tripradar may have evolved between this plan being written and now — re-derive the list from the actual slnx. The pattern is `domains/travel/tripradar/<original-relative-path>`.

- [ ] **Step 3: Delete `domains/travel/tripradar/TripRadar.slnx`**

```
git rm domains/travel/tripradar/TripRadar.slnx
```
Expected: stages the deletion. The slnx is now redundant — its project list was absorbed into `ino.slnx`.

---

## Task 4 — Audit relative paths inside tripradar's source

This is the highest-risk task. Tripradar may have files that traverse to repo-root expecting to find `D:\ino\tripradar\` instead of `D:\ino\domains\travel\tripradar\`. Two known hotspots; check both, then sweep for others.

**Files:**
- Audit: `domains/travel/tripradar/src/Aspire/AppHost.cs`
- Audit: `domains/travel/tripradar/src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs`
- Audit: anywhere else that climbs `..\..\..\..\` style.

- [ ] **Step 1: Grep tripradar for repo-root path climbing**

```
Grep pattern="\\.\\.\\\\.+\\.\\.\\\\.+\\.\\.\\\\" path="domains/travel/tripradar" output_mode="content"
```
(Ripgrep regex for `..\..\..\` style four-level ascents in C# string literals or `Path.Combine` calls.)

Also grep for absolute path remnants:
```
Grep pattern="tripradar" path="domains/travel/tripradar/src" output_mode="content" -i=true
```
Any hit referencing `D:\ino\tripradar` or `\tripradar\` as a hardcoded prefix is a problem; references to `tripradar` as part of resource names (`bot`, `api`, etc.) are fine.

- [ ] **Step 2: Open `AppHost.cs` and verify**

```
Read D:\ino\domains\travel\tripradar\src\Aspire\AppHost.cs
```
Look for:
- `Directory.GetParent(AppContext.BaseDirectory)` chains — count how many `.Parent`s before they expect to find a known repo file.
- `Path.Combine(..., "..", "..", ...)` calls.
- Hardcoded path strings.

For each hit: walk through manually with the new folder depth (was 1 level under `D:\ino\tripradar\src\Aspire\`, now 4 levels under `D:\ino\domains\travel\tripradar\src\Aspire\`). If the code climbs to find a repo-root marker (e.g. `.git/`, `.editorconfig`, `Directory.Build.props`), the climb count needs updating to add 2 more `.Parent` calls — OR better, use the .NET 11 idiom `Path.GetDirectoryName` walking until a marker file is found, which is depth-independent.

If no path-climbing logic exists, this step is a no-op — leave the file unchanged.

- [ ] **Step 3: Open `CloudflaredExtensions.cs` and verify**

```
Read D:\ino\domains\travel\tripradar\src\Aspire\Hosting\Cloudflared\CloudflaredExtensions.cs
```
Same audit as Step 2. Cloudflared typically reads a tunnel token file from a path relative to the AppHost's content root — verify the relative path still resolves correctly, or switch to an absolute path resolved against `AppContext.BaseDirectory`.

- [ ] **Step 4: Check `tripradar/aspire.config.json` if it exists**

```
ls D:\ino\domains\travel\tripradar\aspire.config.json
```
If present, read it. The `apphost-path` is relative to the file itself, so the move doesn't break it (`src/Aspire/Aspire.csproj` still resolves from `domains/travel/tripradar/`). No change needed unless the file uses absolute paths.

If it doesn't exist, skip this step.

- [ ] **Step 5: Sanity build of just the moved folder**

```
dotnet build D:\ino\domains\travel\tripradar\src\Aspire\Aspire.csproj
```
Expected: clean build. If errors point to missing files / unresolved references, that's likely a relative-path issue Steps 2–4 missed.

---

## Task 5 — Update root `D:\ino\CLAUDE.md`

**Files:**
- Modify: `D:\ino\CLAUDE.md`

- [ ] **Step 1: Find every `tripradar/` reference**

```
Grep pattern="tripradar/" path="D:/ino/CLAUDE.md" output_mode="content" -n=true
```
Expected: ~5–8 hits referencing `tripradar/` as a path prefix.

- [ ] **Step 2: Replace `tripradar/` → `domains/travel/tripradar/` for path-context references**

For each hit, decide whether the reference is a **path** (replace) or a **conceptual mention** (leave alone — e.g., "tripradar is the external product"). Path references look like ``tripradar/`` in code blocks or backticks; conceptual mentions are usually freeform text.

Use Edit with `replace_all=false` per occurrence so you can judge each one. Don't blanket-replace.

After edits, sanity-grep again:
```
Grep pattern="\\bD:\\\\ino\\\\tripradar" path="D:/ino/CLAUDE.md" output_mode="content"
Grep pattern="tripradar/" path="D:/ino/CLAUDE.md" output_mode="content"
```
Expected: zero hits for the first (no absolute-path remnants); only conceptual mentions remain for the second.

---

## Task 6 — Update `tripradar/CLAUDE.md` (now at `domains/travel/tripradar/CLAUDE.md`)

**Files:**
- Modify: `domains/travel/tripradar/CLAUDE.md`

- [ ] **Step 1: Update the slnx reference**

The file references `TripRadar.slnx` as the solution file. After the merge, that slnx is gone — use `ino.slnx` (relative to repo root, two levels up).

Edit any occurrence of:
- `TripRadar.slnx` → `D:\ino\ino.slnx` (or `../../../ino.slnx` relative to tripradar)
- `dotnet build src/Aspire/Aspire.csproj` → no change (still works from inside tripradar's folder, just `cd` first)

- [ ] **Step 2: Add a "post-relocation" note at the top of the file**

Add a short paragraph after the title:

```markdown
## Repository location

This product was relocated from `D:\ino\tripradar\` to `D:\ino\domains\travel\tripradar\` on 2026-05-02 as part of the ino Phase 4 epilogue. Tripradar still ships as an independent service with its own Aspire AppHost (`src/Aspire/Aspire.csproj`). Build it from inside this folder or via the merged `D:\ino\ino.slnx`.
```

- [ ] **Step 3: Update LSP config reference**

The file mentions `.lsp.json` configured to use `TripRadar.slnx`. If `domains/travel/tripradar/.lsp.json` still references `TripRadar.slnx`, repoint it to `ino.slnx` (two levels up, relative to the `.lsp.json` location):

```
Read D:\ino\domains\travel\tripradar\.lsp.json
```
If it references `TripRadar.slnx`, edit to `../../../ino.slnx`. If it doesn't, skip.

---

## Task 7 — Verify build of merged solution

- [ ] **Step 1: Restore + build `ino.slnx`**

```
cd D:\ino
dotnet build ino.slnx
```
Expected: clean build of all projects (~64 total: 28 ino + 19 tripradar + iaw projects). Errors here are almost always slnx path mistakes or missing relative-path fixes from Task 4.

If errors mention "project not found", verify each new path in Task 3 actually exists on disk.

If errors mention package conflicts, verify tripradar's `Directory.Packages.props` is still being picked up. The file should be at `domains/travel/tripradar/Directory.Packages.props` — MSBuild walks upward and picks the closest one for tripradar projects.

- [ ] **Step 2: Run `dotnet test ino.slnx`**

```
dotnet test ino.slnx --no-build
```
Expected: all existing tests still pass. If tripradar tests fail, drill into the failing test — likely a Task 4 relative-path miss.

---

## Task 8 — Verify both AppHosts still boot

- [ ] **Step 1: Start ino.AppHost from repo root**

```
cd D:\ino
aspire run
```
Expected: dashboard opens. All ino silos (kernel, identity, travel, taxi, genesis, telegram, etc.) reach Healthy. If any fails, check that no ino code accidentally imported tripradar projects (search `using TripRadar` in `D:\ino\src\` and `D:\ino\domains\` excluding tripradar itself).

Stop with Ctrl+C once Healthy.

- [ ] **Step 2: Start tripradar's AppHost from its new location**

Per `tripradar/CLAUDE.md`, tripradar uses `dotnet run --project ...` (not `aspire run`):

```
cd D:\ino
dotnet run --project domains/travel/tripradar/src/Aspire/Aspire.csproj
```
Expected: tripradar's own dashboard opens; its services (bot, api, jobs, migrations, website) reach Running. If anything fails, that's the Task 4 relative-path audit — fix and retry.

Stop with Ctrl+C once verified.

---

## Task 9 — Commit (3 granular commits per the spec)

- [ ] **Step 1: Stage and commit the folder move + path fixes**

```
git status
```
Verify the staged changes are: the rename of every tripradar file + any modifications to `AppHost.cs`, `CloudflaredExtensions.cs`, etc. from Task 4.

```
git add domains/travel/tripradar/
git commit -m "$(cat <<'EOF'
refactor(repo): move tripradar/ → domains/travel/tripradar/

Pure folder move with relative-path audits inside tripradar's AppHost
and Cloudflared extensions. Tripradar continues to ship as an
independent product with its own Aspire AppHost.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 2: Stage and commit the slnx merge**

```
git add ino.slnx
git rm domains/travel/tripradar/TripRadar.slnx 2>$null
# (the rm may already be staged from Task 3 Step 3 — that's fine)
git commit -m "$(cat <<'EOF'
chore(repo): merge tripradar projects into ino.slnx, delete TripRadar.slnx

All 19 tripradar projects now live under /domains/travel/tripradar/ in
the unified ino.slnx. One solution to rule them all. Tripradar's own
TripRadar.slnx is removed.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 3: Stage and commit the docs updates**

```
git add CLAUDE.md domains/travel/tripradar/CLAUDE.md domains/travel/tripradar/.lsp.json
git status
# verify only docs / config files are staged
git commit -m "$(cat <<'EOF'
docs: update CLAUDE.md tripradar paths after relocation

Both repo CLAUDE.md and tripradar's own CLAUDE.md updated to reflect
the new path domains/travel/tripradar/. Tripradar's .lsp.json points
at ino.slnx now that TripRadar.slnx is gone.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Push**

```
git push
```

---

## Done when

1. `dotnet build ino.slnx` clean.
2. `dotnet test ino.slnx --no-build` clean.
3. `aspire run` from `D:\ino` boots ino's silos to Healthy.
4. `dotnet run --project domains/travel/tripradar/src/Aspire/Aspire.csproj` boots tripradar's dashboard.
5. Three commits on `master`, pushed.

## Out of scope

- Folding tripradar services into Ino.AppHost (separate AppHost preserved).
- Deleting tripradar's `Directory.Packages.props` (deferred — version-conflict audit later).
- Tripradar consuming `Ino.Llm.Xai` or any cross-runtime sharing.
- Cloudflared tunnel config for prod webhooks.
