---
title: DigitalBrain
---

# DigitalBrain

The convenience metapackage. It carries no assemblies of its own — it exists so a service that consumes
a brain needs one reference instead of three.

```xml
<PackageReference Include="DigitalBrain" Version="0.1.0-alpha.1" />
```

Pulls in:

- [`DigitalBrain.Abstractions`](/packages/abstractions) — synapses, identity, the handler interfaces
- [`DigitalBrain.Client`](/packages/client) — `BrainClient`, owner sessions, journal reads
- [`DigitalBrain.Aspire`](/packages/aspire) — `AddDigitalBrainClient` for an Aspire-hosted service

## What it deliberately excludes

It does **not** reference [`DigitalBrain.Kernel`](/packages/kernel). That is the entire design of this
package: the kernel is where provider SDKs and credentials live, so a consumer that takes the obvious
one-line dependency cannot end up with an OpenAI or Anthropic SDK on its graph, and cannot be handed a
model API key.

A process that *hosts* neurons references `DigitalBrain.Kernel` explicitly and knowingly.

`eng/pack.ps1` verifies this on the produced artifacts rather than trusting the project file: it opens
every `.nupkg`, reads the nuspec, and fails the release if any package outside the kernel declares a
provider SDK, or if anything but the testing package declares a dependency on the kernel.
