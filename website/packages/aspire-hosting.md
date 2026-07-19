---
title: DigitalBrain.Aspire.Hosting
---

# DigitalBrain.Aspire.Hosting

Composing a brain in an Aspire AppHost. This package is referenced by the AppHost only, and it never
references `DigitalBrain.Kernel` — the AppHost declares what models exist, it does not load them.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var openAiKey = builder.AddParameter("openai-key", secret: true);

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores()
    .WithModel(ModelTier.Balanced, ModelProviders.OpenAi, "gpt-4.1-mini", openAiKey);

builder.AddProject<Projects.Silo>("silo").WithReference(brain);
builder.AddProject<Projects.Api>("api").WithReference(brain.AsClient());
```

## Two references, on purpose

`WithReference(brain)` is **privileged**. It hands over model bindings and their API keys, and belongs
only to the silo that hosts neurons.

`WithReference(brain.AsClient())` is the projection for everyone else: Orleans client discovery and
safe metadata, no model configuration and no key. The publish manifest is gated by a test asserting no
secret value appears in it.

## Models are declared, not hardcoded

`WithModel(tier, provider, modelId, apiKey)` binds a *role* to a concrete model. The key is an Aspire
parameter, never a literal. Bindings reach the silo as configuration under `DigitalBrain__Models`,
which is what `DigitalBrain.Kernel` reads to build its `IChatClient` per tier.

::: warning Open debt
`AsClient()` currently delegates to the Orleans hosting integration's own client projection, which would
pass a credentialed storage connection string to the referencing service if the brain were configured
with durable Azure stores. It is inert while stores are memory-backed. See [Status](/status).
:::
