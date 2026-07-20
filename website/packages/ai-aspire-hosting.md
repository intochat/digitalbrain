---
title: DigitalBrain.Modules.AI.Aspire.Hosting
---

# DigitalBrain.Modules.AI.Aspire.Hosting

AI-owned AppHost configuration:

```csharp
brain.AddModule<AIModule>(ai => ai
    .WithLlm<Llama32>()
    .WithLlm<Gpt56>());
```

Ollama models share one Ollama resource. OpenAI models share one OpenAI resource and one secret
`openai-api-key` parameter. The parameter description links directly to the OpenAI Platform.

Only silo references receive provider endpoints, model names, and secret parameter expressions.
Client references receive none of them.
