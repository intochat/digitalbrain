# Deterministic Flutter Generated Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make normal Flutter verification reproducible without tracked generated comments or a dirty checkout.

**Architecture:** Git owns authored platform sources while Flutter owns the seven desktop registrants it rewrites. Repository policy enforces the exact ownership boundary, and the standard Flutter workflow proves regeneration rather than relying on a custom wrapper.

**Tech Stack:** Git, Flutter CLI, Dart/Flutter tests, xUnit, .NET 11, Aspire 13.4.6.

## Global Constraints

- Work on `master` and preserve user-authored commit `3dc35fa3`.
- Do not change Aspire or package versions.
- Do not weaken the comment policy.
- Do not add comments to tracked source or configuration.
- Use `aspire start` and `aspire stop` for AppHost lifecycle.
- Run the exact root command `dotnet test --logger "console;verbosity=minimal"` with the AppHost stopped.

---

### Task 1: Enforce Generated-Artifact Ownership

**Files:**

- Modify: `.gitignore`
- Modify: `tests/DigitalBrain.UnitTests/RepositoryPolicyTests.cs`
- Delete from tracking: `app/linux/flutter/generated_plugin_registrant.cc`
- Delete from tracking: `app/linux/flutter/generated_plugin_registrant.h`
- Delete from tracking: `app/linux/flutter/generated_plugins.cmake`
- Delete from tracking: `app/macos/Flutter/GeneratedPluginRegistrant.swift`
- Delete from tracking: `app/windows/flutter/generated_plugin_registrant.cc`
- Delete from tracking: `app/windows/flutter/generated_plugin_registrant.h`
- Delete from tracking: `app/windows/flutter/generated_plugins.cmake`

**Interfaces:**

- Consumes: `TrackedFiles(string root)` and repository-root discovery already owned by `RepositoryPolicyTests`.
- Produces: `FlutterGeneratedArtifacts` and an xUnit fact proving every named path is ignored and untracked.

- [ ] **Step 1: Write the failing ownership test**

Add the exact seven-path collection and a fact that asserts every path is absent from `TrackedFiles(root)` and returns exit code zero from `git check-ignore --no-index -q -- <path>`. Add a small process helper using `ProcessStartInfo.ArgumentList` so paths are never shell-concatenated.

- [ ] **Step 2: Run the test and prove the red state**

Run: `dotnet test tests/DigitalBrain.UnitTests/DigitalBrain.UnitTests.csproj --logger "console;verbosity=minimal"`

Expected: the new ownership test fails because all seven files are tracked and none has an exact ignore rule.

- [ ] **Step 3: Implement the ownership boundary**

Append these exact patterns to `.gitignore` without a prose comment:

```gitignore
/app/linux/flutter/generated_plugin_registrant.cc
/app/linux/flutter/generated_plugin_registrant.h
/app/linux/flutter/generated_plugins.cmake
/app/macos/Flutter/GeneratedPluginRegistrant.swift
/app/windows/flutter/generated_plugin_registrant.cc
/app/windows/flutter/generated_plugin_registrant.h
/app/windows/flutter/generated_plugins.cmake
```

Remove the seven paths from Git tracking and the authored working tree. Do not remove any neighboring platform file.

- [ ] **Step 4: Prove Flutter regeneration is clean**

Run from `app`: `flutter pub get`

Expected: all seven files exist again with Flutter-owned contents, `git check-ignore -v` identifies the exact rules, and `git status --short` does not list them.

- [ ] **Step 5: Run focused verification**

Run the UnitTests project and the Studio golden file twice.

Expected: UnitTests 47/47 and Studio 9/9 on each run. After every Flutter command, `git status --short` contains only the intended authored changes.

- [ ] **Step 6: Run broad verification**

Run `flutter analyze`, then `flutter test` twice from `app`. Stop the AppHost, run `dotnet test --logger "console;verbosity=minimal"` from the repository root, restart the AppHost, run Aspire doctor, and inspect resource health.

Expected: analysis exits zero, Flutter 563/563 twice, root 1002/1002, Aspire doctor 5/0/0, and the checkout remains free of regenerated-file diffs.

- [ ] **Step 7: Commit**

Stage only `.gitignore`, `RepositoryPolicyTests.cs`, and the seven tracked deletions. Verify `git diff --cached --check`, then commit as `fix: make Flutter verification regeneration-safe`.

### Task 2: Independent Acceptance Review

**Files:**

- Review only: the Task 1 commit range and verification transcript.

**Interfaces:**

- Consumes: the exact seven-path invariant and Task 1 evidence.
- Produces: approval or actionable findings before the Feature Studio preservation gate begins.

- [ ] **Step 1: Verify scope and behavior**

Confirm no non-generated platform source disappeared, exact ignore rules are used, policy coverage cannot silently widen, and Flutter regenerates the files before tests/builds.

- [ ] **Step 2: Reproduce the durability claim**

Run a normal Flutter command, the ownership policy, and `git status --short` independently.

Expected: tests pass and the seven ignored files do not dirty the checkout.

- [ ] **Step 3: Accept or return findings**

Reject for any package/product change, broad ignore rule, lost plugin registration, policy exemption, or dirty post-Flutter checkout. Otherwise approve and advance the master continuation plan to the Feature Studio preservation gate.
