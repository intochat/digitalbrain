# IAW Model Pattern And Auth Hardening Execution Plan

Date: 2026-07-09

## Evidence

IAW keeps the AppHost model surface thin: model descriptors live as small provider-specific classes, and the AppHost declares the active tier mapping with `WithLLM<T>().AsFast()`, `AsBalanced()`, `AsReasoning()`, `WithEmbedding<T>()`, and `WithVoice2Text<T>()`.

Brain already has the fluent option API, but its default AppHost still registers `Qwen25Coder1_5B` as the balanced LLM even though that model is marked `ChatOnly`. The active model graph therefore still allows a non-tool-capable model to become the default.

Brain also has provider ids for direct OpenAI and GitHub Models, but runtime construction is incomplete:

- The keyed chat-client path supports Azure OpenAI, Anthropic, xAI, and falls every other provider through to Ollama.
- The unkeyed/global chat-client path supports only Ollama and Azure OpenAI.
- This means declaring OpenAI or GitHub Models in the AppHost would compile only after model classes are added, but would not be reliably usable at runtime.

The Gmail/Salesforce OAuth root cause fixed earlier was identity scope drift between the Ino tool provider and the actual tool execution call. The remaining auth gap is coverage: Salesforce has completion, pending-state, mismatch, no-pending, and two-user tests; Google currently only has OAuth start coverage.

## Implementation Order

1. Add tests first.
   - AppHost/model registry should not register chat-only production LLMs as active roles.
   - OpenAI/GitHub provider registrations should not silently fall through to Ollama.
   - Default/unkeyed provider support should match keyed provider support for configured providers.
   - Google OAuth should promote `google-oauth-pending` into user-scoped `google`, clear pending, reject state mismatches, reject missing pending state, and isolate two users.

2. Clean up model descriptors.
   - Keep abstractions/catalog in `DigitalBrain.Core.Models`.
   - Split concrete production descriptors into provider folders.
   - Remove Qwen production descriptors from active code.
   - Keep chat-only negative coverage through test-local models only.

3. Fix provider runtime.
   - Centralize chat-client construction.
   - Add explicit direct OpenAI and GitHub Models client construction.
   - Add provider secret/env wiring for direct OpenAI and GitHub Models.
   - Make unknown providers fail clearly instead of falling back to Ollama.

4. Refactor Aspire hosting files.
   - Split `DigitalBrainContext`, `DigitalBrainOptions`, model resource setup, registry export, and kernel wiring out of the large builder extension file.
   - Keep existing resource behavior intact unless covered by tests.

5. Update AppHost.
   - Make active defaults tool-capable.
   - Keep cloud provider examples commented but compile-ready when uncommented.
   - Keep local Whisper active.
   - Remove stale comments tied to the Qwen-era decision.

## Done Criteria

- Targeted model/provider/auth tests pass.
- `GatewayServiceSalesforceViaChatIdentityTests` remains green.
- Google OAuth completion has parity with Salesforce coverage.
- AppHost no longer declares any non-tool-capable LLM as an active role.
- Unknown providers fail with a clear error instead of being treated as Ollama.
- Broad compile succeeds for the touched projects.
