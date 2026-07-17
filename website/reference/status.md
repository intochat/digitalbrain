# Implementation status

DigitalBrain is under active kernel-first development. Documentation uses the following labels:

| Label | Meaning |
| --- | --- |
| Running | Present in the current Aspire topology |
| Implemented | Code and automated tests exist |
| Target | Accepted direction, not fully implemented |
| Decision | Architecture is intentionally unresolved |

## Running locally

| Resource | Status |
| --- | --- |
| Kernel host | Running |
| MCP edge | Running |
| UI gateway | Running |
| Ollama and local model | Running |
| VitePress documentation | Running after the website integration in this change |

## Architecture

| Area | Status |
| --- | --- |
| Durable neuron addressing | Implemented baseline |
| Universal invocation envelope | Implemented baseline, targeted for edge-only use |
| Specialized typed neuron contracts | Target |
| Explicit synapse taxonomy | Target |
| Provider-idempotent effect rail | Target |
| Authenticated actor context | Target |
| Module manifest and compatibility gates | Decision |
| Community runtime isolation | Decision |

The status table is deliberately conservative. A homepage claim is not evidence that an invariant is implemented.
