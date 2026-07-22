# Documentation Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce DigitalBrain's documentation from 28 markdown files to 13, rename `website/` to `docs/` as the single source of truth, and replace two overlapping planning records with one module-organized `docs/architecture.md`.

**Architecture:** `docs/tests/site.test.mjs` is the enforcement mechanism that makes "the site is the source of truth" real — it asserts page existence, navigation reachability, and specific content claims. Every task in this plan is test-first against that file: change the assertion, watch it fail, change the documentation, watch it pass. No source assertion is deleted without a replacement covering the same claim. No planning record is deleted until its content has a proven destination.

**Tech Stack:** Markdown, VitePress 1.6.4, Node 24 `node:test`, PowerShell, git.

## Global Constraints

- Design spec of record: `docs/superpowers/specs/2026-07-22-documentation-consolidation-design.md`.
- No changes to any `.cs`, `.csproj`, `.slnx`, or `Directory.Packages.props` file. The root gate result must be identical before and after this plan.
- Never run `dotnet test` with `--filter`.
- The website gate invokes `node` directly, never through `npm` — npm's cmd children lose the nodejs PATH on Windows here.
- Relative paths only in all committed content. Never reference a path under a user profile directory.
- Markdown prose is documentation, not a comment; the no-comments rule does not apply to it.
- Branch: `agent/gmail-salesforce-enrichment`. Do not create a new branch. Do not merge or push.
- Session snapshot for the ground-moved check: HEAD was `2f21c9b4` and the tree was clean when this plan was written; the spec commit `00c1641c` sits on top of it.
- Every module `Status:` line uses exactly `Status: Built` or `Status: Designed` — the literal string, sentence case, no trailing period.
- Commit at every task boundary with the three diff-grill answers in the message body.

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `docs/packages.md` | One table covering all 19 shipped packages. Replaces twelve files under `packages/`. |

**Rewritten in place**

| Path | Responsibility |
|---|---|
| `docs/architecture.md` | The single architecture document. Eleven sections; module-organized §4. |
| `docs/concepts.md` | The vocabulary page. Absorbs `CONTEXT.md`. |
| `docs/tests/site.test.mjs` | Retargeted and rewritten assertions. The enforcement mechanism. |
| `docs/.vitepress/config.mts` | Nav, sidebar, and `srcExclude`. |

**Modified**

| Path | Change |
|---|---|
| `CLAUDE.md` | §1 and §7 plan-of-record pointer; §5 gate path. |
| `README.md` | Plan-of-record link, status paragraph, gate path, repository-shape block. |
| `docs/contributing.md` | Gate section becomes a pointer; plan-of-record reference. |
| `.github/workflows/ci.yml` | `working-directory: website` → `docs`. |
| `docs/tools/render-specification.mjs` | Variable name only; path arithmetic is already relative and correct. |
| `docs/superpowers/plans/2026-07-20-foundation-poc.md` | Trimmed to outstanding Tasks 9–12; Task 12 file list updated. |

**Deleted**

| Path | Task |
|---|---|
| `../APPROVED-ARCHITECTURE-DECISIONS.md` (outside the repository) | 1 |
| `docs/superpowers/plans/2026-07-21-task-5-codex-continuation-prompt.md` | 1 |
| `CONTEXT.md` | 3 |
| `docs/packages/` (12 files) | 4 |
| `docs/status.md` | 6 |
| `APPROVED-ARCHITECTURE-DECISIONS.md` | 7 |
| `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` | 7 |

---

## Task 1: Remove dead session artifacts

**Files:**
- Delete: `../APPROVED-ARCHITECTURE-DECISIONS.md` (one directory above the repository root)
- Delete: `docs/superpowers/plans/2026-07-21-task-5-codex-continuation-prompt.md`

**Interfaces:**
- Consumes: nothing
- Produces: nothing. This task removes files that nothing references.

Neither file is referenced by any `.cs`, `.csproj`, `.yml`, or `.mjs` file. The out-of-repository copy is a stale duplicate that contradicts the in-repository record on decisions D4.6, D5.10, D5.15, and D7.2. The continuation prompt is a handoff for plan Task 5, which landed across commits `2d8bad34`, `33889fe2`, `6fcbb734`, `18f9721c`, and `9ce4f834`.

- [ ] **Step 1: Record the session snapshot**

```powershell
git rev-parse HEAD
git status --porcelain
```

Expected: `00c1641c…` and empty output. If the tree is dirty with changes you did not make, stop and surface them.

- [ ] **Step 2: Prove nothing references the continuation prompt**

```powershell
rg -n "task-5-codex-continuation" --glob '!.git'
```

Expected: no matches.

- [ ] **Step 3: Delete both files**

```powershell
Remove-Item ..\APPROVED-ARCHITECTURE-DECISIONS.md
git rm docs/superpowers/plans/2026-07-21-task-5-codex-continuation-prompt.md
```

- [ ] **Step 4: Verify the out-of-repository copy is gone and the in-repository one remains**

```powershell
Test-Path ..\APPROVED-ARCHITECTURE-DECISIONS.md
Test-Path .\APPROVED-ARCHITECTURE-DECISIONS.md
```

Expected: `False` then `True`.

- [ ] **Step 5: Commit**

```powershell
git commit -m "docs: remove stale duplicate and spent handoff prompt"
```

Commit body must answer: added with no consumer (nothing); claimed without checking (nothing — deletion verified with Test-Path, references verified with rg); changed that I did not change (nothing).

---

## Task 2: Rename website to docs with the gate green

**Files:**
- Rename: `website/` → `docs/` (all 20 entries including `.vitepress/`, `tools/`, `tests/`, `packages/`, `public/`)
- Modify: `docs/tests/site.test.mjs:8` and every `read('website', …)` call
- Modify: `docs/tools/render-specification.mjs:7`
- Modify: `.github/workflows/ci.yml:52`
- Modify: `CLAUDE.md` §5 website gate block
- Modify: `README.md` gate block and repository-shape block

**Interfaces:**
- Consumes: nothing
- Produces: the `docs/` path that every later task uses. After this task the website gate is run from `docs/`, not `website/`.

`docs/superpowers/` already exists and now merges into the same tree as the VitePress source. `srcExclude` is added in Task 4 alongside the other config changes; until then VitePress would render the plans, but `node --test` does not build the site, so the gate stays meaningful.

- [ ] **Step 1: Perform the rename**

```powershell
git mv website/.vitepress docs/.vitepress
git mv website/tools docs/tools
git mv website/tests docs/tests
git mv website/packages docs/packages
git mv website/public docs/public
git mv website/index.md docs/index.md
git mv website/quickstart.md docs/quickstart.md
git mv website/concepts.md docs/concepts.md
git mv website/architecture.md docs/architecture.md
git mv website/contributing.md docs/contributing.md
git mv website/status.md docs/status.md
git mv website/package.json docs/package.json
git mv website/package-lock.json docs/package-lock.json
```

- [ ] **Step 2: Confirm website/ is empty and remove it**

```powershell
Get-ChildItem website -Force -ErrorAction SilentlyContinue
Remove-Item website -Recurse -Force -ErrorAction SilentlyContinue
```

Expected: no output from the first command.

- [ ] **Step 3: Run the site gate to watch it fail**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: `render-specification.mjs` **succeeds** — its path arithmetic is relative (`resolve(toolsDirectory, '..')` then `'..', 'tests', 'DigitalBrain.Simulations'`) and survives the rename unchanged. `node --test` **fails** with `ENOENT` on `…/website/package.json`, because `site.test.mjs` joins the repository root to the literal `'website'`.

- [ ] **Step 4: Retarget the test file**

In `docs/tests/site.test.mjs`, change line 8 and rename the binding in the same edit:

```javascript
const docsRoot = join(repositoryRoot, 'docs')
```

Rename every remaining use of `websiteRoot` to `docsRoot` — it appears in the `existsSync` calls of the tests `documentation project exposes the standard VitePress commands` and `every documented page exists and nothing else claims to be documentation`. Every later task in this plan refers to the binding as `docsRoot`.

Then replace every `read('website', …)` call with `read('docs', …)` using a single find-and-replace of the exact string `read('website', ` with `read('docs', `.

- [ ] **Step 5: Rename the stale binding in the renderer**

In `docs/tools/render-specification.mjs`, rename `websiteRoot` to `docsRoot` in all four places it appears (declaration on line 7, the `simulations` resolve on line 8, and the `writeFileSync` join at the end). This is a cosmetic rename; the resolved paths are unchanged.

- [ ] **Step 6: Run the site gate to watch it pass**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: all tests pass. Record the exact pass count.

- [ ] **Step 7: Update CI**

In `.github/workflows/ci.yml`, in the `website` job, change `working-directory: website` to `working-directory: docs`. Rename the job key `website:` to `docs:` on line 48.

- [ ] **Step 8: Update the two gate blocks**

In `CLAUDE.md` §5, change `Set-Location website` — if present — and any `website/` path in the website gate block to `docs`. In `README.md`, change `cd website` to `cd docs` in the website gate block, and change the `website/   VitePress documentation and the published specification` row of the repository-shape block to `docs/      VitePress documentation and the published specification`.

- [ ] **Step 9: Prove no stale path references remain**

```powershell
rg -n "website" --glob '!.git' --glob '!package-lock.json'
```

Expected: no matches outside `CHANGELOG.md` historical entries. If `docs/contributing.md` still says "documentation site has its own gate" with a `website` path, fix it here.

- [ ] **Step 10: Commit**

```powershell
git add -A
git commit -m "docs: rename website to docs"
```

---

## Task 3: Merge CONTEXT.md into docs/concepts.md

**Files:**
- Modify: `docs/concepts.md`
- Modify: `docs/tests/site.test.mjs` — the test named `concepts define the three primitives and the scope fence`
- Delete: `CONTEXT.md`

**Interfaces:**
- Consumes: `docs/concepts.md` from Task 2
- Produces: the vocabulary page that `docs/architecture.md` links to for term definitions in Tasks 5–6.

`CONTEXT.md` is a 116-line glossary of 30 terms across Core, AI, Work, and Time, each with an `_Avoid_:` line naming the words that must not be used for that concept. Those `_Avoid_:` lines are the repository's strongest anti-drift device and are carried across verbatim.

- [ ] **Step 1: Extend the concepts test to demand the glossary**

In `docs/tests/site.test.mjs`, replace the body of the test `concepts define the three primitives and the scope fence` with:

```javascript
test('concepts define the three primitives and the whole vocabulary', () => {
  const concepts = read('docs', 'concepts.md')

  assert.match(concepts, /IHandle<TSynapse>/)
  assert.match(concepts, /IEmit<TSynapse>/)
  assert.match(concepts, /journaled grain/)
  assert.match(concepts, /correlation and causation lineage/)
  assert.match(concepts, /dev-only/)

  const glossaryTerms = [
    'Neuron', 'Synapse', 'Capability request', 'Module', 'Behavior', 'Registry',
    'LLM', 'Agent', 'Orchestration', 'Group Chat', 'Participant', 'Executor', 'Capability',
    'Task', 'Goal', 'Attempt', 'Worker', 'Workflow', 'Blocker', 'Result', 'Successor Task',
    'Countdown', 'Reminder', 'Interval schedule', 'Calendar schedule', 'Occurrence',
  ]
  for (const term of glossaryTerms) {
    assert.ok(concepts.includes(`**${term}**`), `concepts must define ${term}`)
  }

  const avoidLines = concepts.match(/^_Avoid_: /gm) ?? []
  assert.ok(avoidLines.length >= 26, `every glossary term needs an _Avoid_ line, found ${avoidLines.length}`)
})
```

- [ ] **Step 2: Run the test to watch it fail**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: FAIL with `concepts must define Capability request`.

- [ ] **Step 3: Merge the glossary into concepts.md**

Keep the existing `docs/concepts.md` prose sections `Neuron`, `Synapse`, `Module`, and `Simulation` as the narrative introduction. Delete its final `## Scope` section — the scope claim now lives in `docs/architecture.md` §4 module status lines.

Append a `## Vocabulary` section containing all four groups from `CONTEXT.md` (`Core`, `AI`, `Work`, `Time`) as `### ` subsections, with each of the 30 term blocks copied verbatim in the form:

```markdown
**Neuron**:
A durable, addressable identity that receives requests and facts and owns its operational state.
_Avoid_: Service, actor, grain
```

Reformat the `_Avoid_:` lines so each begins at the start of its own line, which the test's `^_Avoid_: ` anchor requires. Do not reword any definition.

- [ ] **Step 4: Run the test to watch it pass**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: PASS.

- [ ] **Step 5: Delete CONTEXT.md and prove nothing references it**

```powershell
git rm CONTEXT.md
rg -n "CONTEXT\.md" --glob '!.git'
```

Expected: matches only inside `docs/superpowers/plans/2026-07-20-foundation-poc.md`, which Task 8 rewrites.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "docs: fold the glossary into the concepts page"
```

---

## Task 4: Collapse the package pages into one table

**Files:**
- Create: `docs/packages.md`
- Delete: `docs/packages/` (12 files)
- Modify: `docs/.vitepress/config.mts`
- Modify: `docs/tests/site.test.mjs` — `packagePages`, `contentPages`, the navigation test, and the test named `every shipped package has a page, and the boundary is stated`

**Interfaces:**
- Consumes: `docs/packages/index.md` boundary prose from Task 2
- Produces: `/packages` as a single route. Task 6 relies on this route existing when it rewrites the sidebar's remaining entries.

The site documents 11 packages. `tests/DigitalBrain.Tests/PackableProjects.cs` lists 18, plus the `DigitalBrain` metapackage. Google, Salesforce, and Tasks have no page at all. One table makes the drift class impossible rather than merely fixed.

- [ ] **Step 1: Rewrite the package assertions**

In `docs/tests/site.test.mjs`, delete the `packagePages` array. Change `contentPages` to:

```javascript
const contentPages = [
  'index.md', 'concepts.md', 'architecture.md', 'quickstart.md', 'contributing.md',
  'packages.md', 'specification.md',
]
```

Add `'packages'` to the `retiredSections` array so the old tree cannot come back:

```javascript
const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference', 'packages']
```

Do not add `'status.md'` here. `docs/status.md` still exists until Task 6, and `retiredSections` asserts non-existence — adding it now makes this task's own gate fail.

Replace the whole test `every shipped package has a page, and the boundary is stated` with:

```javascript
test('every shipped package is in the table, and the boundary is stated', () => {
  const packages = read('docs', 'packages.md')
  const packableSource = read('tests', 'DigitalBrain.Tests', 'PackableProjects.cs')

  const packable = [...packableSource.matchAll(/"(DigitalBrain[^"]*)"/g)].map(match => match[1])
  assert.ok(packable.length >= 18, `expected the packable list, found ${packable.length}`)

  for (const name of packable) {
    assert.ok(packages.includes(`\`${name}\``), `the table must list ${name}`)
  }

  assert.ok(packages.includes('`DigitalBrain`'), 'the table must list the metapackage')
  assert.match(packages, /Provider SDKs live only in `DigitalBrain\.Modules\.AI`/)
  assert.match(packages, /does \*\*not\*\* reference `DigitalBrain\.Kernel`/)
  assert.match(packages, /refuses to start/i)
  assert.match(packages, /namespace and type name are the model identity/i)
  assert.match(packages, /openai-api-key/)
})
```

In the navigation test, replace the `/packages/` link with `/packages` and delete the `for (const page of packagePages)` loop entirely.

- [ ] **Step 2: Run the test to watch it fail**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: FAIL with `ENOENT` on `docs/packages.md`.

- [ ] **Step 3: Write docs/packages.md**

Create `docs/packages.md` with front matter `title: Packages`, the boundary prose carried from `docs/packages/index.md`, and one table with a row per package. The five regex assertions above must each find their claim; carry those claims from the pages being deleted — `metapackage.md` states the metapackage does **not** reference `DigitalBrain.Kernel`, `kernel.md` states the kernel refuses to start under a named condition, `ai.md` states the namespace and type name are the model identity, `ai-aspire-hosting.md` names the `openai-api-key` parameter.

Table shape, one row per package, all 19:

```markdown
| Package | Contains | Depends on |
| --- | --- | --- |
| `DigitalBrain` | Consumer metapackage | Abstractions, Client, Aspire |
| `DigitalBrain.Abstractions` | Leaf neuron and synapse contracts | nothing |
| `DigitalBrain.Kernel` | Domain-neutral silo runtime | Abstractions |
```

Continue for `DigitalBrain.Client`, `DigitalBrain.Testing`, `DigitalBrain.Aspire`, `DigitalBrain.Aspire.Hosting`, `DigitalBrain.DevTools`, then the three AI packages, the three Google packages, the three Salesforce packages, and the two Tasks packages. Read each `.csproj` under `src/` and `modules/` to state the real dependency rather than guessing. Do not invent an `Aspire.Hosting` package for Tasks — it has none.

- [ ] **Step 4: Delete the old tree and update navigation**

```powershell
git rm -r docs/packages
```

In `docs/.vitepress/config.mts`, change the nav entry `{ text: 'Packages', link: '/packages/' }` to `{ text: 'Packages', link: '/packages' }`. Replace the entire `Packages` sidebar group — the group header plus all twelve items — with a single item `{ text: 'Packages', link: '/packages' }` inside the `Start here` group.

Add `srcExclude` at the top level of the config object, immediately after `cleanUrls: true`:

```javascript
  srcExclude: ['superpowers/**'],
```

- [ ] **Step 5: Run the test to watch it pass**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "docs: collapse the package pages into one table"
```

---

## Task 5: Write the architecture document's kernel, modules, and status lines

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/tests/site.test.mjs` — the test named `the architecture page separates what is built from what is designed`

**Interfaces:**
- Consumes: `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` §2 and `APPROVED-ARCHITECTURE-DECISIONS.md` §§1–7, both still present on disk
- Produces: `docs/architecture.md` §§1–7 and the `Status:` line convention. Task 6 appends §§8–11 to the same file. Task 7 deletes the two source records once Task 6 proves every rule has a destination.

Sections 1 through 7 of the eleven-section structure fixed in the spec. Module subsections carry the only status claims left in the repository.

- [ ] **Step 1: Replace the architecture assertions**

In `docs/tests/site.test.mjs`, replace the test `the architecture page separates what is built from what is designed` with:

```javascript
test('the architecture page is module-organized and states each status once', () => {
  const architecture = read('docs', 'architecture.md')

  for (const heading of [
    'The vision', 'The kernel', 'The module model', 'The modules',
    'Behaviors and scripting', 'Registry and discovery', 'Hosting and durability',
  ]) {
    assert.ok(architecture.includes(heading), `architecture must have a ${heading} section`)
  }

  for (const module of ['AI', 'Tasks', 'Google', 'Salesforce', 'Time', 'Flutter', 'Memory']) {
    assert.ok(
      new RegExp(`### .*\\b${module}\\b`).test(architecture),
      `architecture must have a ${module} module section`)
  }

  const built = architecture.match(/^Status: Built$/gm) ?? []
  const designed = architecture.match(/^Status: Designed$/gm) ?? []
  assert.equal(built.length, 4, 'AI, Tasks, Google, and Salesforce are built')
  assert.equal(designed.length, 2, 'Time and Flutter are designed')

  assert.match(architecture, /human-approved proposal/)
  assert.match(architecture, /Runtime behavior installation is designed and not yet built/)
  assert.doesNotMatch(architecture, /REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE/)
})
```

- [ ] **Step 2: Run the test to watch it fail**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: FAIL with `architecture must have a The vision section`.

- [ ] **Step 3: Write sections 1 through 7**

Replace `docs/architecture.md` entirely. Keep its existing accurate code samples — the `Llama32` constructor, the AppHost block, the silo block, the `DigitalBrainClient.Connect` block, and the `Analyst` neuron — but note that the `Analyst` sample calls `llama.AskAsync(...)`, which the ratified MEAI boundary replaced. Rewrite that sample to the `RespondAsync(IReadOnlyList<ChatMessage>)` wire before reusing it, and verify the signature against `modules/DigitalBrain.Modules.AI.Contracts/` rather than trusting this plan.

Structure:

```markdown
# Architecture

## 1. The vision
## 2. The kernel
## 3. The module model
## 4. The modules
### 4.1 AI
### 4.2 Tasks
### 4.3 Google
### 4.4 Salesforce
### 4.5 Time
### 4.6 Flutter
### 4.7 Memory
## 5. Behaviors and scripting
## 6. Registry and discovery
## 7. Hosting and durability
```

Content sources, distilled rather than copied:

| Section | Source |
|---|---|
| 1 The vision | `CLAUDE.md` §1 and `README.md` opening |
| 2 The kernel | `REFINED` §2.1 — neuron mechanics, the forbidden list, `CapabilityRequested` causal protocol, `CapabilityDelegation` |
| 3 The module model | `REFINED` §2.2 and §2.3 — package triple, namespace vocabulary, AppHost selection, generated catalog |
| 4.1 AI | `REFINED` §2.4, §2.5, §2.6, §2.10 |
| 4.2 Tasks | `REFINED` §2.11 and §2.12 |
| 4.3 Google | `REFINED` §2.7 integration rules, Gmail specifics |
| 4.4 Salesforce | `REFINED` §2.7 mutation ledger and reconciliation |
| 4.5 Time | `REFINED` §2.13 |
| 4.6 Flutter | `REFINED` §6 item 6 |
| 4.7 Memory | `REFINED` §2.14 and §6 item 7 |
| 5 Behaviors | `REFINED` §2.9 |
| 6 Registry | `REFINED` §2.8 |
| 7 Hosting | `REFINED` §2.13 Aspire profile and §2.10 observability |

Each of §§4.1–4.6 opens with its `Status:` line as the first line of the subsection body. §4.7 Memory carries no status line — it is out of scope, not designed — which is why the test expects exactly four Built and two Designed.

Do not carry any completion percentage, test count, or "next up" sentence into this file.

- [ ] **Step 4: Run the test to watch it pass**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: PASS.

- [ ] **Step 5: Verify the status lines by eye**

```powershell
rg -n "^Status: (Built|Designed)$" docs/architecture.md
```

Expected: exactly six matches, in the order Built, Built, Built, Built, Designed, Designed.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "docs: write the module-organized architecture"
```

---

## Task 6: Add limitations, ratified rules, rejections, and build order

**Files:**
- Modify: `docs/architecture.md` — append §§8–11
- Modify: `docs/tests/site.test.mjs` — repoint the tests named `status stays truthful about the rebuild` and `the open debts are disclosed rather than buried`
- Modify: `docs/.vitepress/config.mts` — remove Status from nav and sidebar
- Delete: `docs/status.md`

**Interfaces:**
- Consumes: `docs/architecture.md` §§1–7 from Task 5; `APPROVED-ARCHITECTURE-DECISIONS.md` §9 (47 numbered rules) and §8 (open items)
- Produces: the complete architecture document. Task 7 may delete the two planning records only after this task passes.

`docs/status.md` fuses progress reporting with genuine limitation disclosure. The reporting is provably false — its table claims Google and Salesforce are not built while `modules/` contains both. The seven open debts concern the system that is built and are carried across with their test guard intact.

- [ ] **Step 1: Repoint the two status tests at the architecture page**

In `docs/tests/site.test.mjs`, delete the test `status stays truthful about the rebuild` entirely — every claim it makes is either a gate command now owned by `CLAUDE.md` or a progress statement being deleted. Replace the test `the open debts are disclosed rather than buried` with:

```javascript
test('the open debts are disclosed rather than buried', () => {
  const architecture = read('docs', 'architecture.md')

  assert.match(architecture, /trusted cluster peer/)
  assert.match(architecture, /Journal history is bounded/)
  assert.match(architecture, /Effectively-once processing is also windowed/)
  assert.match(architecture, /FIFO per target/)
  assert.match(architecture, /Delivery ordering/)
  assert.match(architecture, /Broadcast addressing/)
  assert.match(architecture, /handler \*\*types\*\*/)
  assert.match(architecture, /timeline stream/)
  assert.match(architecture, /AsClient/)
  assert.match(architecture, /DevUI/)
})

test('the ratified rules survive as a checklist', () => {
  const architecture = read('docs', 'architecture.md')

  const numbered = architecture.match(/^\d+\. /gm) ?? []
  assert.ok(numbered.length >= 47, `expected 47 ratified rules, found ${numbered.length}`)

  for (const rejected of ['Ical.Net', 'Durable Extension', 'model tier', 'raw invoke']) {
    assert.ok(architecture.includes(rejected), `the rejected list must name ${rejected}`)
  }
})
```

- [ ] **Step 2: Run the tests to watch them fail**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: FAIL on `trusted cluster peer` — the debts are still in `status.md`, not `architecture.md`.

- [ ] **Step 3: Append section 8, Known limitations**

Carry all seven debts from `docs/status.md` §Open debts into `docs/architecture.md` as `## 8. Known limitations`, preserving the bold lead-in of each and the wording the test pins. The seven, in order: an Orleans client is a trusted cluster peer; journal history is bounded and effectively-once processing is windowed; delivery ordering is local FIFO per target; broadcast targets handler **types**; client observation is not the final timeline stream; `AsClient()` needs a production credential audit; DevUI is not part of the current architecture.

- [ ] **Step 4: Append section 9, Ratified rules**

Carry all 47 numbered rules from `APPROVED-ARCHITECTURE-DECISIONS.md` §9 into `## 9. Ratified rules`, preserving its six subheadings and the numbering 1–47:

| Subheading | Rules |
|---|---|
| Kernel and modules | 1–5 |
| AI and MAF | 6–18 |
| Behaviors | 19–23 |
| Integrations and MCP | 24–30 |
| Tasks | 31–40 |
| Time and hosting | 41–47 |

Copy the rule text verbatim. Keep the framing sentence: if code contradicts a rule, the code is wrong unless the decision is reversed in writing. This step is the mitigation for the spec's headline risk — every rule has a destination before any source file is deleted.

- [ ] **Step 5: Append sections 10 and 11**

`## 10. Open, and explicitly rejected` carries `APPROVED` §8's open list — the internal calendar recurrence library, Memory architecture, and the exact Time record shapes — and a rejected list naming at minimum Ical.Net with Noda Time, the MAF Durable Extension, model tiers and routing, and any raw invoke escape hatch. Add the rejections already in the current `docs/architecture.md` §Rejected: AI logic in the kernel, provider routing tiers and balancing, public model metadata definitions, runtime module scanning, raw MCP clients crossing module boundaries, a second client facade, and compatibility shims.

`## 11. Build order` carries `REFINED` §6's seven deferred items in dependency order. No checkboxes, no percentages, no dates.

- [ ] **Step 6: Delete status.md and its navigation**

```powershell
git rm docs/status.md
```

In `docs/.vitepress/config.mts`, remove `{ text: 'Status', link: '/status' }` from `nav`, and remove the Status item from the `Project` sidebar group, leaving Contributing as that group's only item.

In `site.test.mjs`, remove `'/status'` from the navigation test's link list, and add `'status.md'` to `retiredSections` — now correct, because this task deletes the file:

```javascript
const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference', 'packages', 'status.md']
```

- [ ] **Step 7: Run the tests to watch them pass**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "docs: carry limitations and ratified rules into architecture"
```

---

## Task 7: Delete the planning records and repoint every reference

**Files:**
- Delete: `APPROVED-ARCHITECTURE-DECISIONS.md`
- Delete: `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`
- Modify: `CLAUDE.md` §1 and §7
- Modify: `README.md`
- Modify: `docs/contributing.md`

**Interfaces:**
- Consumes: the complete `docs/architecture.md` from Task 6
- Produces: `docs/architecture.md` as the sole plan-of-record pointer across the repository.

Deletion is safe only now: Task 5 gave §§1–7 a destination and Task 6 proved the 47 rules and the limitations landed, with tests asserting both.

- [ ] **Step 1: Prove the rule count survived before deleting the source**

```powershell
rg -c "^\d+\. " docs/architecture.md
```

Expected: at least 47.

- [ ] **Step 2: Delete both records**

```powershell
git rm APPROVED-ARCHITECTURE-DECISIONS.md REFINED-ARCHITECTURE-AND-NEXT-STEPS.md
```

- [ ] **Step 3: Repoint CLAUDE.md**

In §1, replace the paragraph beginning `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md is the plan of record.` with a pointer to `docs/architecture.md`, keeping the instruction to read the ratified architecture before changing framework code and to record any reversal in that file rather than silently reversing it. In §7, replace the `REFINED` reference the same way. Leave the `docs/superpowers/plans/2026-07-20-foundation-poc.md` reference intact — Task 8 keeps that file.

- [ ] **Step 4: Repoint README.md**

Replace the plan-of-record paragraph with a link to `docs/architecture.md`. In the Status section, replace the sentence listing what is built and not built with a pointer to the module status lines in `docs/architecture.md` — README must not carry its own status claim. Update the two `website/architecture.md` and `website/status.md` links in the opening and Status paragraphs to `docs/architecture.md`.

- [ ] **Step 5: Repoint docs/contributing.md**

In the `## The gate` section, delete the root-gate block — the `dotnet test --logger "console;verbosity=minimal"` command and its surrounding paragraph — and the website-gate block, replacing both with one sentence pointing at `CLAUDE.md` §5 as the canonical gate.

Keep exactly three things in that section: the release command `dotnet test .\DigitalBrain.slnx -c Release`, the `--filter` prohibition and its one-line reason, and the Tier 0 / Tier 1 / Tier 2 explanation. Keep the comments rule wherever it currently sits.

`site.test.mjs` asserts all five of `dotnet test \.\\DigitalBrain\.slnx -c Release`, `--filter`, `Comments are forbidden`, `Tier 0`, `Tier 1`, and `Tier 2` against this file. Those assertions are unchanged by this task and must keep passing — do not edit the contributing test.

Replace the `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` reference with `docs/architecture.md`.

- [ ] **Step 6: Prove no reference survives**

```powershell
rg -n "REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE" --glob '!.git'
```

Expected: matches only in `docs/superpowers/plans/2026-07-20-foundation-poc.md`, which Task 8 rewrites, and in `docs/superpowers/specs/2026-07-22-documentation-consolidation-design.md`, which is a historical design record and keeps them.

- [ ] **Step 7: Run the site gate**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "docs: retire the two planning records"
```

---

## Task 8: Trim the foundation PoC plan

**Files:**
- Modify: `docs/superpowers/plans/2026-07-20-foundation-poc.md`

**Interfaces:**
- Consumes: `docs/architecture.md` for the plan-of-record pointer
- Produces: the live plan, reduced to outstanding work.

Tasks 1 through 8 of that plan are built — `modules/` contains AI, Google, Salesforce, and Tasks. Tasks 9 through 12 are outstanding: `rg` finds no `ICountdown` and no `WithAzureStorage` in any `.cs` file.

- [ ] **Step 1: Re-verify what is outstanding rather than trusting this plan**

```powershell
rg -l "ICountdown|WithAzureStorage" --glob '*.cs'
Get-ChildItem modules -Directory -Name
```

Expected: no `.cs` matches; the module list shows AI, Google, Salesforce, and Tasks families and no Time family. If either expectation is wrong, stop and re-scope this task.

- [ ] **Step 2: Delete the completed task sections**

Remove the sections `## Task 1:` through `## Task 8:` inclusive — from line 444 through the line immediately before `## Task 9:`. Keep everything above line 444: `Scope lock`, `Fixed dependency direction`, `Frozen PoC contracts`, `Capability-tool boundary`, and `TDD and commit protocol`. Keep `## Task 9:` through `## Task 12:`, the `Requirement-to-proof map`, and `Stop conditions`.

- [ ] **Step 3: Record what was removed**

Immediately after the plan's goal paragraph, add one sentence stating that Tasks 1 through 8 are complete and that git history holds their detail, naming the range `2d8bad34..2f21c9b4`.

- [ ] **Step 4: Update Task 12's file list**

In `## Task 12`, replace the **Files:** block with the consolidated set:

```markdown
- Modify: `docs/architecture.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/concepts.md`
```

Replace the website gate block inside Task 12 with:

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```

- [ ] **Step 5: Repoint the remaining references**

Within the file, replace every `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`, `APPROVED-ARCHITECTURE-DECISIONS.md`, and `CONTEXT.md` reference with `docs/architecture.md` or `docs/concepts.md` as appropriate.

- [ ] **Step 6: Prove the plan renders nothing to the site**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
rg -n "srcExclude" docs/.vitepress/config.mts
```

Expected: tests pass; `srcExclude: ['superpowers/**']` present from Task 4.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "docs: trim the foundation plan to outstanding work"
```

---

## Task 9: Final verification

**Files:** none modified.

**Interfaces:**
- Consumes: every preceding task
- Produces: the evidence that permits a completion claim.

- [ ] **Step 1: Count the surviving markdown files**

```powershell
Get-ChildItem -Recurse -Filter *.md |
  Where-Object { $_.FullName -notmatch '\\(\.git|node_modules|\.superpowers|\.claude|\.codex|\.grok|\.config)\\' } |
  Select-Object -ExpandProperty FullName
```

Expected: exactly 13 — `CLAUDE.md`, `README.md`, `AGENTS.md`, `CHANGELOG.md`, `docs/index.md`, `docs/quickstart.md`, `docs/concepts.md`, `docs/architecture.md`, `docs/packages.md`, `docs/contributing.md`, `docs/specification.md`, `docs/superpowers/plans/2026-07-20-foundation-poc.md`, `docs/superpowers/specs/2026-07-22-documentation-consolidation-design.md`.

- [ ] **Step 2: Prove no stale reference survives**

```powershell
rg -n "REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE|CONTEXT\.md|website/" --glob '!.git' --glob '!*/specs/*'
```

Expected: no matches outside `CHANGELOG.md` historical entries.

- [ ] **Step 3: Prove the status convention holds**

```powershell
rg -c "^Status: (Built|Designed)$" docs/architecture.md
```

Expected: 6.

- [ ] **Step 4: Run the site gate and record counts**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```

Expected: zero failures. Quote the exact pass count.

- [ ] **Step 5: Run the root gate and record counts**

```powershell
dotnet test --logger "console;verbosity=minimal"
```

Expected: identical to the pre-work result — no `.cs` file was touched. Quote exact pass, fail, and skip counts. Zero failures and zero skips are required.

- [ ] **Step 6: Check whether the ground moved**

```powershell
git rev-parse HEAD
git status --porcelain
git diff --check
```

Compare against the snapshot in Global Constraints. Surface any change made by something other than this plan rather than absorbing it into a commit.

- [ ] **Step 7: Confirm the site still builds**

```powershell
Set-Location docs
node --run build
Set-Location ..
```

If `node --run build` is unavailable, run `npx vitepress build` from `docs/`. Expected: build succeeds and does not emit pages for anything under `superpowers/`. If the build emits `superpowers/**` pages, `srcExclude` from Task 4 Step 4 is missing or misspelled.

---

## Notes for the implementer

**The one genuinely hard task is 5.** Sections 1–7 distil roughly 1,100 lines of ratified architecture into readable module-organized prose. The temptation is to paste `REFINED` §2 wholesale under new headings. That reproduces the problem this plan exists to solve. Write each module section so a reader who has never seen the planning records understands what the module owns, what it must never do, and what is settled versus open.

**Do not trust this plan's line numbers over the file.** `REFINED` §2 subsection numbers and the `## Task 1:` line number in the foundation plan were read at authoring time on commit `00c1641c`. Re-read before cutting.

**`site.test.mjs` gets weaker before it gets stronger.** Tasks 4, 5, and 6 each delete assertions before adding their replacements. If a task is abandoned midway the guard is genuinely reduced, so finish or revert a task rather than leaving it half-applied.

**The `Analyst` code sample in the current architecture page is stale.** It calls `llama.AskAsync(request.Prompt)`, which the ratified MEAI boundary removed in favour of `RespondAsync(IReadOnlyList<ChatMessage>)`. Verify the real signature in `modules/DigitalBrain.Modules.AI.Contracts/` before reusing the sample. Do not propagate the stale call.
