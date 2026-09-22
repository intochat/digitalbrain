# AI neurons and conversational agents

Approved direction: the September 21 proposal, amended September 22 to use the conversational actor interface in `E:/projects/IAW/src/Core/Contracts/IAgent.cs`. The user explicitly requested implementation.

## Public model

An agent is a durable conversational actor with configuration, metadata, history, response/stream methods, usage, state and cancellation. Each agent key owns its conversation. Existing Ask callers remain compatible. Individual executions have persisted run identifiers/status and typed signals; callers do not need to manage a conversation/run trio to ask a question. Scheduling, callbacks tied to a UI, workspace management and behavior compilation are stage 2.

LLMs become callable neurons with optional provider-specific marker interfaces. Provider adapters own transport. Model identity, capabilities, limits and request settings are distinct. Explicit unsupported settings fail. SDK runtime objects do not cross neuron interfaces. Inference does not execute tools; agents own tool invocation. Image generation, embeddings, transcription and speech synthesis have separate operation contracts.

Agents snapshot configuration per execution, preserve structured messages and tool call identities, and reject conflicting conversation mutations while running. Cancellation remains callable during execution. Failed or abandoned turns do not become successful history. Persist state before lifecycle signals; live observer signals are not a durable event log. Interrupted activations are recorded, not automatically replayed across tool side effects.

Tool references select registered native/MCP tools explicitly; connections and credentials stay in host services. Missing or ambiguous tools fail. Execution has finite deadlines and tool iteration limits. Model capabilities do not grant tool authorization.

## Implementation choices

Public run records are exposed through IAgent.GetState; the IAW-style facade does not require a separate IAgentRun neuron. Existing IConversation remains compatible for application coordination. Common inference options are supplemented by typed OpenAI/Ollama settings. Unverified model limits remain host-declared/unknown. Media operations use configured default transports and bounded inline bytes because this layer has no resolvable artifact store. Image editing, realtime duplex voice and per-request media model routing are documented extension points rather than advertised implementations.

## Verification

Use deterministic adapters for unit tests and hosted integration tests. Cover streaming, cancellation, history, isolation, configuration revisions, tool failures, option validation, serialization and actual provider request mapping. Real external credentials are not required for the default suite. Preserve existing consumers and run their build/tests where feasible.