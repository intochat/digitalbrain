# Behavior block implementation verification

Implemented on `codex/behavior-blocks`, based on `9caf473357b621c16c890140c53f98766f65ecb5`.

## Delivered

Natural-language behavior cards with a contextual menu, independent description editing, duplication/deletion, explicit many-to-many scenario links, and source sheets. Registry neurons/signals are highlighted and inspectable; ambiguous names retain candidates. Drafts use revision-checked structured documents inside the existing neuron. Published packages retain stable identities in `app.authoring.json`; `app.spec.md` remains the deterministic gate input. Legacy specs remain available with explicit conversion. Build and publish retains the existing lifecycle.

## Checks

- Flutter analyze: no issues.
- Flutter tests: 112 passed, including light/dark cards, exact revision requests, parser/highlighter/status projections, editing, source disclosure, conflict recovery and narrow header layout.
- Apps unit: 88 passed.
- CSharp unit: 73 passed.
- Apps E2E: 4 passed. Includes authenticated structured import/save, stale conflict, unauthorized import and revision-specific source access, alongside existing sharing/runtime coverage.
- CSharp E2E: 2 passed.
- Final release web build: passed (69.6 seconds). Existing optional Cupertino font warning remains.
- `git diff --check`: clean.

An independent read-only branch review found three actionable P2 issues. Each received a regression that failed before repair and passed afterward: scenario dialog reload after conflict; accessible build attempt diagnostics; pairing failed-rebuild source inspection with the last published/imported bindings. No review minors were deferred. Visual inspection also found the narrow published header squeezed its title; a regression reproduced an overflow and passed after actions moved into a wrapping row.

The initial full Apps E2E run exposed test-account collisions with an existing fixture. Distinct account names fixed the isolation problem; the complete suite then passed. The expanded vocabulary fixture initially misapplied an interface-only attribute to a signal record; it now tests non-public signal exclusion and passes.

## Rendered UI evidence

Actual Flutter widgets were exercised in a local browser harness using explicitly labeled fixture data, at desktop and 390-pixel width in both themes. Inspected scenario expansion, menus, focused editor/scenario linking, exact-revision source sheet, registry detail, and the published header. The fixture's source text and verification results are illustrative; these screenshots are not evidence of a live generated research app. Backend lifecycle behavior was exercised separately by unit/HTTP integration tests. Full keyboard/screen-reader audits and live-model generation quality were not assessed.

Screenshots in the chat output directory: `behavior-blocks-desktop.png`, `behavior-blocks-narrow.png`, `behavior-blocks-dark-narrow.png`, `behavior-blocks-published-narrow.png`, `behavior-blocks-source.png`, `behavior-blocks-registry.png`, `behavior-blocks-editor.png`.

## Host smoke limitation

The standard `aspire run` could not bind the occupied resource-service port 22146. An isolated AppHost started successfully; after its model download and on-demand sandbox start, 47 of 48 reported resources were Healthy, including IntoChat, Flutter, Apps and CSharp. Its MCP process exited because port 5081 was already occupied. Existing listeners were not stopped or changed. The smoke AppHost was stopped after inspection. This is a partial host smoke, not an all-resources-healthy claim.

## Rulings made during native execution

1. Used a native Git worktree because the desktop worktree API cannot operate from this projectless chat. Cost: the app cannot manage this checkout's lifecycle.
2. Appended typed fields with legacy defaults and string vocabulary kinds. Cost if incompatible: an adapter change; old data must fall back, never imply verified success.
3. Kept the metadata path in the document codec rather than runtime package enumeration. Cost: authoring owns this storage convention.
4. Combined persistence/build changes and later UI changes into larger commits because their contracts overlap. Deferred HTTP proof to the final task. Cost: coarser review/rollback boundaries.
5. Preserved imported operation/account metadata and accepted explicit Builder operations instead of replacing every app with an ask-only manifest. Cost: Builder output needs validation through existing manifest rules.
6. Legacy conversion proposes unassigned scenarios and requires explicit behavior creation/linking instead of inventing source ownership. Cost: extra authoring work for legacy apps; no automatic grouping.
7. Shared presentation returns children for a parent-owned lazy list rather than nesting a scroller. Cost: parents must supply scrolling correctly.
8. Kept mutations/import on the existing draft neuron and timestamps on AppVerification. Cost: no independent scenario-neuron lifecycle; consistent with the approved model.
9. Failed rebuild source inspection retains the last published/imported source revision while attempt diagnostics identify the failed revision separately. Cost: rejected implementation source is not inspectable through the behavior's source menu.
10. Used a fixture harness for repeatable responsive visual checks and a separate actual AppHost smoke. Cost: visual evidence does not validate live-model generation or the full authenticated production journey.

No deferred review minors. The branch and worktree remain available for integration.

## PR 128 review follow-up

An independent review of pushed commit `5fddf6234` found two further issues: conflict recovery restored obsolete source bindings, and embedded tabs in scenario names conflicted with the existing failure-result protocol. Both were reproduced by failing regressions and repaired. Behavior saves now apply editable fields onto the latest behavior record; scenario validation rejects tabs. GitHub CI also identified seven whitespace violations in the codec; the formatter repaired them.

After these corrections: Flutter 113 tests passed, Apps unit 89 passed, Flutter analyzer clean, targeted C# whitespace verification passed, and the entire PR diff passes `git diff --check`. Earlier CSharp and integration results remain the evidence for unchanged paths. GitHub CI reruns against the correction commit. The review and fix summary are posted on PR 128.
