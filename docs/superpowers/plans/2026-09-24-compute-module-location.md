# Compute Module Location Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move the Compute module next to Apps under `src/Modules/DigitalBrain/Compute` without changing its runtime or public APIs.

**Architecture:** Move the existing implementation, contracts, tests, and README directories intact. Update relative project references and solution paths while keeping assembly identities, namespaces, resource names, and module composition unchanged.

**Tech Stack:** .NET 11, C#, MSBuild, Aspire, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-24-compute-module-location.md`

## Global Constraints

- Preserve assembly names, namespaces, public APIs, resource names, and behavior.
- Keep all Compute project assets under `src/Modules/DigitalBrain/Compute`.
- Do not alter unrelated pre-existing workspace changes.

## Review Focus

- Moved projects still resolve the kernel, Inbox contracts, and shared test projects from their new locations.
- All consumers use the new Contracts and implementation project paths.
- The embedded price book retains its logical resource name.
- Solution project entries point only to the moved files.
- No stale `src/Modules/Compute` references remain.

### Task 1: Move Compute project tree and update references

**Files:**
- Move: `src/Modules/Compute/Compute/**` to `src/Modules/DigitalBrain/Compute/Compute/**`
- Move: `src/Modules/Compute/Contracts/**` to `src/Modules/DigitalBrain/Compute/Contracts/**`
- Move: `src/Modules/Compute/Tests/**` to `src/Modules/DigitalBrain/Compute/Tests/**`
- Move: `src/Modules/Compute/README.md` to `src/Modules/DigitalBrain/Compute/README.md`
- Modify: `DigitalBrain.slnx`
- Modify references in AppHost, IntoChat, E2E, AI, Broker, Connections, and Receipts project files
- Modify relative project references inside the moved Compute project files

- [x] Move the directories with Git-aware moves, preserving all file contents. Git had no indexed Compute paths in this working tree, so moved the explicit directories and retained normal rename detection for VCS.
- [x] Update every project reference to the new `DigitalBrain/Compute` location.
- [x] Update the solution entries and group Compute under `/Modules/DigitalBrain/Compute/`.
- [x] Search for any remaining `src/Modules/Compute` references in source and solution files; none remain.
- [x] Build Compute and its direct project consumers: AppHost, Connections tests, and IntoChat E2E.
- [x] Run the Compute unit test project (29 passed, 1 database-dependent skipped) and `git diff --check`.
