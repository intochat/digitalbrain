---
title: Packages
---

# Packages

DigitalBrain ships as small packages with one hard rule between them: **provider SDKs and credentials
live only in `DigitalBrain.Kernel`.** Nothing a client, a test, or an Aspire AppHost references can
drag an OpenAI or Anthropic SDK — or the API key that goes with it — onto its compile or runtime graph.
`eng/pack.ps1` opens every produced `.nupkg` and fails the release if that boundary is breached.

| Package | Reference it when | Depends on |
| --- | --- | --- |
| [`DigitalBrain`](/packages/metapackage) | You consume a brain and want one reference | Abstractions, Client, Aspire |
| [`DigitalBrain.Abstractions`](/packages/abstractions) | You define synapses shared by both sides | Orleans.Sdk |
| [`DigitalBrain.Kernel`](/packages/kernel) | You host a silo that runs neurons | Abstractions, Orleans server, provider SDKs |
| [`DigitalBrain.Client`](/packages/client) | You talk to a brain from outside the cluster | Abstractions, Orleans client |
| [`DigitalBrain.Testing`](/packages/testing) | You write simulations | Client, Kernel, Orleans TestingHost, Reqnroll |
| [`DigitalBrain.Aspire`](/packages/aspire) | Your consuming service is Aspire-hosted | Client, Orleans client |
| [`DigitalBrain.Aspire.Hosting`](/packages/aspire-hosting) | You compose the brain in an AppHost | Abstractions, Aspire.Hosting |
| [`DigitalBrain.DevTools`](/packages/devtools) | You want a dashboard and in-memory journals in dev | Orleans Dashboard, Orleans Journaling |

All packages are versioned together and released as `0.1.0-alpha.1`. The prerelease suffix is not
decoration: the framework pins the Orleans `10.2.2-rc.2` line including the experimental
`Microsoft.Orleans.Journaling` package, and a stable version may not depend on a prerelease.

## The boundary, concretely

A silo process references `DigitalBrain.Kernel` and is the only place a model API key is ever
configured. A consuming service references `DigitalBrain` (or `DigitalBrain.Client` directly) and gets
Orleans client discovery and typed neuron access — no model binding, no secret. In an AppHost,
`WithReference(brain)` is the privileged form and is meant for the silo; `brain.AsClient()` is the
projection you hand to everything else.

::: warning Open debt
`AsClient()` currently delegates to the Orleans hosting integration's own client projection. If a brain
were configured with durable Azure stores, that projection would pass a credentialed connection string
to the referencing service. It is inert today because the AppHost composes memory-backed stores, but it
is a real leak on the path to production. See [Status](/status).
:::
