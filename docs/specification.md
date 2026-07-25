---
title: Specification
---

# Specification

Executable product proofs live in the test tiers:

| Tier | Project | What it proves |
| --- | --- | --- |
| L0 | `tests/DigitalBrain.Tests` | Package graph, assembly boundaries, public shapes, capability reification, AI surface pins |
| L1 | `tests/DigitalBrain.TestingTests` | Testing product lifecycle (fixture, journals, clock, faults) |
| L1 | `tests/DigitalBrain.Quickstart.Tests` | External author greeter durability |
| L1 | `tests/DigitalBrain.Time.Tests` | Durable `ICountdown` lifecycle and recovery (Orleans reminder as wake authority) |
| L1 | `tests/DigitalBrain.Tasks.Tests` | Task Start/Cancel lifecycle via test-only `IWorker` |
| L1 | `tests/DigitalBrain.ModuleTests` | Typed LLM smoke; Concurrent/GroupChat Respond multi-participant + session reuse |
| L1 | `tests/DigitalBrain.Integrations.Tests` | Gmail ReadMessage admit + annotation refusal; Salesforce propose/reject/approve on scripted MCP edge; AccountEnrichment multi-module sample loop |
| L1 | `tests/DigitalBrain.Flutter.Tests` | Flutter vocabulary L1 journals (`IShell` / `IScene` facts) |
| L1 | `tests/DigitalBrain.Ui.Tests` | C# northbound HTTP edge + SSE shell events |
| L1 | `tests/DigitalBrain.Compositions.Tests` | Pre-Behavior-rail OS compositions (shell/countdown/AI pane) over `IDigitalBrain` + contracts only |
| L2 | `tests/DigitalBrain.HostTests` | Exclusive AppHost graph health |

This page is authored markdown, not generated from feature files. Restore a generated specification
only when product Behaviors are again expressed as durable, author-facing scenarios. Behavior
proposal/install remains designed and unbuilt — these tiers prove modules, edges, and samples, not
installed Behaviors.

See also: `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md`.
