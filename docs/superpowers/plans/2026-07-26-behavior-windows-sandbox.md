# Behavior Windows Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute unknown admitted Behavior artifacts in a verified Windows LPAC process whose only useful authority is one bounded, per-execution capability-broker channel.

**Architecture:** A Windows-only adapter creates the runtime worker with `STARTUPINFOEX`, LPAC security capabilities, atomic Job assignment, child-process restriction, mitigation policy, and exact ACLs. The trusted host exposes unary Protobuf gRPC over a `LOCAL\` Kestrel named pipe; the worker loads only the admitted artifact and approved contract assemblies, uses standalone Orleans serialization, and exits after one execution.

**Tech Stack:** Self-contained .NET 10 `win-x64`, Microsoft.Windows.CsWin32 0.3.298, Windows AppContainer/LPAC and Job Objects, ASP.NET Core/Kestrel HTTP/2 named pipes, Grpc.AspNetCore/Grpc.Net.Client/Grpc.Tools 2.80.0, Google.Protobuf 3.31.1, Orleans Serialization 10.2.2-rc.2.

## Global Constraints

- LPAC plus a non-breakaway Job Object is the local-owner/community-code tier, not a hostile hosted multi-tenant boundary.
- Hosted mutually hostile tenants require Hyper-V isolation, a dedicated VM, or an equivalent proven boundary.
- Runtime workers are self-contained, one process per execution, have no child processes, and are terminated with the entire Job on deadline/protocol violation.
- A runtime worker receives no Orleans client/proxy, Azure credential, provider SDK, repository, signing key, general network, or writable artifact directory.
- Artifact and worker files are read/execute-only for the exact AppContainer SID; only a bounded execution temp/profile path is writable.
- Named pipe DACL grants only broker identity and the exact AppContainer SID; `CurrentUserOnly` alone is insufficient.
- One-use bootstrap authorization binds protocol version, execution, owner, Behavior, revision digest, nonce, and deadline.
- Protobuf is the fixed control envelope; opaque CLR payload bytes use standalone Orleans serialization with exact generated codecs.
- The trusted broker service never invokes module grains directly; it reenters `BehaviorNeuron`, then the non-Neuron broker grain and existing delegation filter path.
- Candidate BDD uses the same worker/sandbox/broker path as production execution.
- Non-Windows production registration fails closed; a fake launcher is test-only.

---

## Responsibility Index

| Responsibility | Tasks | Record | Acceptance proof |
| --- | --- | --- | --- |
| Protocol and containment | 1–3 | [Protocol and containment](2026-07-26-behavior-windows-sandbox-protocol-and-containment.md) | The preserved task-specific focused proofs. |
| Broker and worker | 4–5 | [Broker and worker](2026-07-26-behavior-windows-sandbox-broker-and-worker.md) | The preserved task-specific focused proofs. |
| Admission and failure proof | 6–7 | [Admission and failure proof](2026-07-26-behavior-windows-sandbox-admission-and-failure-proof.md) | The preserved task-specific focused proofs and root gates. |

## End-to-End Trust Model

`approved revision → verified immutable files → LPAC/Job worker → exact-SID named-pipe broker → BehaviorNeuron → non-Neuron broker grain → existing delegation filter → module/provider`

The three records retain the predecessor's exact files, interfaces, rationale, sample code, commands, thresholds, red/green acceptance clauses, and task-local commit boundaries. Tasks execute in numeric order; no later record authorizes an earlier trust boundary.

> **Status:** Designed/current. This index and every responsibility record remain a plan. They make no executable or Built claim for the hostile-code boundary.
