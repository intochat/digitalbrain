---
title: Packages
---

# Packages

DigitalBrain separates its domain-neutral runtime from independently shipped domain modules.

| Package | Purpose |
| --- | --- |
| [`DigitalBrain`](/packages/metapackage) | Consumer metapackage |
| [`DigitalBrain.Abstractions`](/packages/abstractions) | Leaf neuron and synapse contracts |
| [`DigitalBrain.Kernel`](/packages/kernel) | Domain-neutral silo runtime |
| [`DigitalBrain.Client`](/packages/client) | Typed owner-bound client |
| [`DigitalBrain.Testing`](/packages/testing) | Real-cluster simulations |
| [`DigitalBrain.Aspire`](/packages/aspire) | Client Generic Host integration |
| [`DigitalBrain.Aspire.Hosting`](/packages/aspire-hosting) | Core AppHost brain composition |
| [`DigitalBrain.DevTools`](/packages/devtools) | Development journals and dashboard |
| [`DigitalBrain.Modules.AI.Contracts`](/packages/ai-contracts) | Provider-free AI neuron contracts |
| [`DigitalBrain.Modules.AI`](/packages/ai) | Typed model neurons and provider adapters |
| [`DigitalBrain.Modules.AI.Aspire.Hosting`](/packages/ai-aspire-hosting) | AI provider resources and parameters |

Provider SDKs live only in `DigitalBrain.Modules.AI`. Provider Aspire integrations live only in
`DigitalBrain.Modules.AI.Aspire.Hosting`. Kernel and all consumer-path packages remain provider-free.
