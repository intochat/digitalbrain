---
title: DigitalBrain.Modules.AI
---

# DigitalBrain.Modules.AI

The AI runtime owns concrete model neurons and provider adapters:

```csharp
public sealed class Llama32(
    [Llm<Llama32>] IChatClient chatClient)
    : LLM(chatClient), ILlama32;
```

`Llama32` uses OllamaSharp. `Gpt56` uses OpenAI through Microsoft.Extensions.AI. Each chat client is
keyed by its concrete model neuron type. The namespace and type name are the model identity.

This package may reference provider SDKs. Kernel and AI Contracts may not.
