# PR 21 Product Vision and CI Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a durable interactive DigitalBrain product-vision prototype to PR #21 and restore its failing Flutter analyzer job.

**Architecture:** The product vision is a single dependency-free HTML document derived from the approved Capability OS UX specification. The CI repair completes the Forui 0.24 migration at the four failing call sites without changing application behavior or weakening analysis.

**Tech Stack:** Semantic HTML, CSS, vanilla JavaScript, Flutter 3.44.6, Dart 3.11, Forui 0.24.0, GitHub Actions.

## Global Constraints

- Keep the HTML self-contained with no external scripts, stylesheets, fonts, or images.
- Represent Home, Chat, Features, Studio, Connectors, Runs, and Memory in one coherent Capability OS shell.
- Demonstrate the unsupported company-research request becoming a durable Feature proposal and opening Studio only with permission.
- Keep trusted Memory separate from Chat history and capability discovery.
- Preserve accessibility, keyboard navigation, responsive layout, reduced-motion behavior, and visible focus states.
- Fix only the Forui 0.24 analyzer breakages and the reported null-aware collection lint.
- Do not edit generated Flutter plugin registrants.
- Do not weaken analyzer settings, dependency constraints, or tests.
- Do not add comments to tracked Dart, YAML, JSON, C#, Proto, PowerShell, XML, or MSBuild files.

---

### Task 1: Interactive product-vision document

**Files:**
- Create: `docs/digitalbrain-capability-os-product-vision.html`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-07-14-digitalbrain-capability-os-product-ux-design.md`
- Produces: One portable HTML file that opens directly from disk and supports navigation and the proposal-to-Studio journey.

- [ ] **Step 1: Create the semantic application shell**

  Include a persistent rail, responsive mobile navigation, contextual page header, global command surface, main workspace, and compact conversation/progress layer.

- [ ] **Step 2: Implement the product surfaces**

  Provide representative, polished states for operational Today, Chat, Features, Studio, Connectors, Runs, and governed Memory. Use product vocabulary from the approved specification and make the company-research Feature proposal the central walkthrough.

- [ ] **Step 3: Add bounded interactions**

  Navigation buttons switch views, Feature cards open Studio, command submission reveals the missing-capability proposal, approval opens Studio, tabs and drawers expose living BDD and C# concepts, and Escape closes transient layers.

- [ ] **Step 4: Verify the document**

  Open the file in the in-app browser, inspect a desktop and narrow viewport, exercise navigation and the company-research journey, check the console, and confirm the document has no network dependencies.

### Task 2: Forui 0.24 CI repair

**Files:**
- Modify: `app/lib/app.dart`
- Modify: `app/lib/ui_kit/ui_date_field.dart`
- Modify: `app/lib/ui_kit/ui_icon.dart`
- Modify: `app/lib/rfw_host/rfw_runtime_host.dart`

**Interfaces:**
- Consumes: Forui 0.24 `FThemeData`, `FDateSelectionController`, `FDateSelectionControl`, and `FLucideIcons` APIs.
- Produces: Flutter sources accepted by `flutter analyze` on Flutter 3.44.6 without behavior regressions.

- [ ] **Step 1: Preserve the observed red state**

  Use GitHub Actions run `29352097857` as the failing baseline: one invalid `FThemeData.copyWith(colors:)`, four invalid date-control operations, eight invalid `FIcons` references, and one `use_null_aware_elements` lint.

- [ ] **Step 2: Repair theme construction**

  Build a new `FThemeData` from the customized `FTheme.neutral.dark.desktop.colors` and set `touch: false`; do not pass global colors through `copyWith`.

- [ ] **Step 3: Repair date ownership**

  Own `FDateSelectionController<DateTime?>` in the widget state, initialize it with `FDateSelectionController.single()`, retain its listener and disposal lifecycle, and pass it through `FDateSelectionControl.managedSingle(controller: _ctrl)`.

- [ ] **Step 4: Repair concrete icon lookup**

  Map names to the corresponding `FLucideIcons` `IconData` constants and retain the circle fallback.

- [ ] **Step 5: Apply the analyzer lint**

  Replace the nullable `if` collection element with the null-aware collection element syntax.

- [ ] **Step 6: Verify Flutter and CI**

  Run formatting, `flutter analyze`, `flutter test`, and `flutter build web` with Flutter 3.44.6 where available. Push and inspect the new PR checks; do not claim local Flutter 3.41.6 is equivalent to the CI toolchain.

### Task 3: PR review and handoff

**Files:**
- Review all files changed by `origin/master...HEAD`.

**Interfaces:**
- Consumes: Completed Tasks 1 and 2 plus the existing PR #21 diff.
- Produces: A pushed PR branch, current check status, and a findings-first review.

- [ ] **Step 1: Review task diffs**

  Check specification coverage, accessibility, security boundaries, Forui API correctness, analyzer strictness, generated-file safety, and accidental scope expansion.

- [ ] **Step 2: Run completion verification**

  Confirm clean Git status, validate the HTML DOM and interactions, run all locally supported checks, and inspect GitHub Actions after push.

- [ ] **Step 3: Commit and push**

  Commit the HTML and CI repair with intentional messages and push `ci/enforce-full-editorconfig` to update PR #21.

- [ ] **Step 4: Perform whole-PR review**

  Review the complete `origin/master...HEAD` diff and report Critical, Important, and Minor findings with exact files and lines. If there are no findings, state that explicitly and list residual verification risks.
