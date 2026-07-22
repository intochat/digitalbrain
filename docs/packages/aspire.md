---
title: DigitalBrain.Aspire
---

# DigitalBrain.Aspire

Client-side Generic Host integration:

```csharp
builder.AddDigitalBrainClient(owner: "acme");
```

The extension configures the Orleans client and registers the single owner-bound
`DigitalBrainClient`. It receives only `brain.AsClient()` from AppHost and no AI settings, provider
resources, or secrets.
