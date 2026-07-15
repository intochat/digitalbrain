# Bounded Feature Approval History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound retained Superseded Feature approvals without deleting current authorization evidence or blocking reset.

**Architecture:** Add a pure `FeatureApprovalLedger.Compact(FeatureApprovalState[])` normalizer with deterministic serialized-byte accounting. Invoke it at every approval growth/supersession point while preserving the existing reservation, actor, and reset invariants.

**Tech Stack:** C# 13, .NET 11 preview, xUnit, explicit `System.Text.Encoding.UTF8` accounting.

## Global Constraints

- Mandatory non-Superseded records are never compacted or dropped.
- The total ledger targets 64 records and 4 MiB; mandatory current records may soft-overflow and then retain zero history.
- Superseded history strips only `Release.Source` and retains the newest deterministic prefix.
- Reset never fails solely because history is full.

---

### Task 1: Specify ledger retention with failing tests

**Files:**
- Create: `tests/DigitalBrain.OrleansTests/Features/FeatureApprovalLedgerTests.cs`
- Modify: `tests/DigitalBrain.OrleansTests/Features/FeatureDraftAuthoringTests.cs`

**Interfaces:**
- Consumes: `FeatureApprovalState`, `FeatureHubTransitions`, and `FeatureDraftAuthoringTransitions`.
- Produces: executable expectations for `FeatureApprovalLedger.Compact(FeatureApprovalState[])` and `FeatureApprovalLedger.SerializedBytes(IEnumerable<FeatureApprovalState>)`.

- [x] Add direct tests named `Compaction_strips_source_only_from_superseded_records`, `Compaction_retains_the_newest_history_within_the_total_record_target`, `Compaction_stops_at_the_first_newest_record_that_exceeds_the_byte_target`, `Mandatory_current_records_soft_overflow_and_evict_all_history`, and `Large_source_is_stripped_while_large_grants_count_toward_the_budget`.
- [x] Extend the real reset/reverify/reproposal test so a full historical ledger still resets, compacts, and permits an exact fresh approval.
- [x] Add a decision test proving 257-character and control-containing IDs are rejected without state mutation.
- [x] Run `dotnet test tests/DigitalBrain.OrleansTests/DigitalBrain.OrleansTests.csproj --no-restore --filter "FullyQualifiedName~FeatureApprovalLedgerTests|FullyQualifiedName~FeatureDraftAuthoringTests"` and verify RED because `FeatureApprovalLedger` and its behavior do not exist.

### Task 2: Implement and integrate deterministic compaction

**Files:**
- Create: `src/DigitalBrain.Kernel/Features/FeatureApprovalLedger.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureLimits.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureHubTransitions.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureDraftAuthoringTransitions.cs`

**Interfaces:**
- Produces: `internal static FeatureApprovalState[] Compact(FeatureApprovalState[] approvals)` and `internal static int SerializedBytes(IEnumerable<FeatureApprovalState> approvals)`.

- [x] Add `ApprovalLedgerRecords = 64` and `ApprovalLedgerUtf8Bytes = 4 * 1024 * 1024` limits.
- [x] Compact Superseded records with `Release = Release with { Source = null }`; order candidates by descending `Revision`, then ordinal `ApprovalId`, then original index; retain the newest prefix only while total record and serialized-byte targets hold; return retained records in original order.
- [x] Invoke `Compact` after proposal append, after decision mutation, and immediately after reset supersession.
- [x] Run the Task 1 filter and verify GREEN.
- [x] Run focused transition, grain, and authoring orchestration tests; then run `git diff --check` and review only the intended files.
