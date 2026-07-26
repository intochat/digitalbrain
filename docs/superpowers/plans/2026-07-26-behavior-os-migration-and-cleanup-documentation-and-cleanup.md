# Behavior OS Migration and Cleanup — Documentation and cleanup

> **Status:** Designed/current. This responsibility record is part of the [OS migration/cleanup plan index](2026-07-26-behavior-os-migration-and-cleanup.md); it records future work and does not claim a live Behavior rail or completed parity cutover.

### Task 6: Rewrite current documentation and archive superseded decisions

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/index.md`
- Modify: `docs/architecture.md`
- Modify: `docs/concepts.md`
- Modify: `docs/packages.md`
- Modify: `docs/quickstart.md`
- Modify: `docs/specification.md`
- Modify: `docs/contributing.md`
- Modify: `docs/.vitepress/config.mts`
- Move: `docs/superpowers/specs/2026-07-25-behavior-os-*.md` → `docs/archive/superpowers/specs/`
- Move: `docs/superpowers/specs/2026-07-25-200-grill-scorecard.md` → `docs/archive/superpowers/specs/`
- Move: `docs/superpowers/specs/2026-07-25-test-truth-scorecard.md` → `docs/archive/superpowers/specs/`
- Test: `docs/tests/content.test.mjs`
- Test: `docs/tests/links.test.mjs`

**Interfaces:**
- Consumes: actual final package graph, runtime APIs, tests, and security claims.
- Produces: current reader docs plus clearly labeled historical evidence.

- [ ] **Step 1: Strengthen documentation truth tests**

```javascript
test("current docs describe the built Behavior rail", async () => {
  const current = await readCurrentDocs();
  assert.doesNotMatch(current, /Behavior rail.*unbuilt|pre-rail compositions|compiled OS Behavior/i);
  assert.match(current, /BehaviorNeuron/);
  assert.match(current, /LPAC/);
  assert.match(current, /human approval/);
});
```

- [ ] **Step 2: Run docs tests and verify failure**

Run: `npm --prefix docs test`

Expected: FAIL on stale current-state claims.

- [ ] **Step 3: Rewrite current docs from the code**

Document framework/modules/OS ownership, packages, one-file authoring, compiler/admission, artifact
identity, catalog installation, event/intent execution, capability replay, Windows security tier,
assistant proposal/approval boundary, hosting, test strategy, and both product paths. Separate
“Built” from the only deferred item: hostile hosted multi-tenant isolation/vector adoption.

Archive superseded grills/scorecards with a generated archive index stating they are historical
decision evidence and may name deleted types. Update links from the approved 2026-07-26 spec.

- [ ] **Step 4: Run docs tests and build**

Run: `npm --prefix docs test; npm --prefix docs run build`

Expected: both exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add README.md CLAUDE.md docs
git commit -m "docs: describe the built behavior operating system"
```

### Task 7: Remove repository trash and prove one coherent system

**Files:**
- Modify only where audit finds an intentional current-state defect.
- Delete any discovered empty directory, checked-in output, temporary artifact, dead helper, obsolete sample, duplicate current doc, or commented-out implementation.
- Modify: `docs/architecture/behavior-os-implementation-ledger.md`

**Interfaces:**
- Consumes: all five plans.
- Produces: completed ledger and root proof with no unowned residual.

- [ ] **Step 1: Audit retired names and forbidden runtime shortcuts**

Run:

```powershell
rg -n "OpenHomeOnActivationBehavior|IAccountEnrichment|EnrichmentModule|DigitalBrain\.Compositions|ActivateDigitalBrain|BootOnActivation|OpenHome|PostAuthBootstrap" src modules hosts os samples tests README.md CLAUDE.md docs --glob "!docs/archive/**" --glob "!docs/research/**" --glob "!docs/superpowers/plans/**"
rg -n "Type\.FullName|GetType\(\)\.FullName" src/DigitalBrain.Kernel src/DigitalBrain.SourceGeneration
rg -n "DispatchProxy|MethodInfo\.Invoke|IClusterClient|Microsoft\.Orleans\.Client" hosts/DigitalBrain.BehaviorWorker src/DigitalBrain.Behaviors.Windows
rg -n "NotImplementedException|throw new NotSupportedException|HACK|TEMP" src modules hosts os samples tests
git ls-files | rg "(^|/)(bin|obj|TestResults|artifacts|\.vs|\.idea)/|\.user$|\.suo$"
```

Expected: no unintended matches. Fix an actual match by deleting or replacing its cause; do not
weaken the search.

- [ ] **Step 2: Audit project graph and filesystem ownership**

Run:

```powershell
dotnet sln DigitalBrain.slnx list
dotnet list DigitalBrain.slnx reference
dotnet list DigitalBrain.slnx package --include-transitive
git status --short
```

Verify every project has a documented owner, every direct package is used, all Orleans versions are
coherent, no empty project/folder remains, and status contains only the current audit edits.

- [ ] **Step 3: Run the complete release gate**

Run:

```powershell
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

Expected: every command exits `0`; no test filter or skip hides a failing project.

- [ ] **Step 4: Complete the ledger with exact commits and evidence**

Replace every `pending` cell in `docs/architecture/behavior-os-implementation-ledger.md` with the
completion commit and root-gate evidence date. Link the security threat model and the final OS BDD
features.

- [ ] **Step 5: Commit the audit**

```powershell
git add .
git commit -m "chore: finish behavior operating system repository audit"
```

- [ ] **Step 6: Verify the final commit is clean**

Run: `git status --short; git log -6 --oneline`

Expected: no status entries and the five slice completions plus final audit are visible.
