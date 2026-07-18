# DigitalBrain.Client

The typed application-facing client for a DigitalBrain cluster.

- `DigitalBrainClient.Get<TNeuron>()` returns the owner-bound Orleans grain reference for a typed capability interface. The authenticated `BrainOwnerId` is the complete grain key; application code never passes a grain key.
- `BrainOwnerContext` carries the authenticated owner, and the outgoing call filter stamps it onto every Orleans request so the kernel can authorize server-side.
- `AddDigitalBrainClient` wires the client, owner context, and call filter onto an Orleans `IClientBuilder`.

This package contains no OpenAI, Anthropic, journal-storage, or development-tool dependencies. Provider SDKs and credentials live only inside the privileged `DigitalBrain.Kernel`.
