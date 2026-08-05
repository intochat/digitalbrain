# Scenario 20: Web research brief with citation table

## User intent
Owner asks for a research brief on a prospect's market position with cited sources, a comparison table, and optional save to memory/Salesforce notes — every claim row tied to a URL from web search, not freeform hallucination.

## Trigger
Chat `UserMessaged`: "Brief me on Contoso's market position vs Fabrikam; cite sources."

## Imagined modules
- WebSearch
- Assistant
- Memory
- Salesforce (notes)
- Charting/Tables
- Chat / Shell

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | Q&A surface |
| assistant/owner | Plan queries; assemble brief |
| websearch/default | Multi-query search answers |
| memory/owner | Optional save brief |
| salesforce/owner-org | Optional note attach |
| shell/primary | Citation table pane |

## Synapse choreography
1. `UserMessaged` → assistant **broadcasts** `ResearchBriefRequested` (topic, entities[]).
2. Assistant **directs** multiple `WebSearchAsked` (company A, company B, market size, recent news).
3. Each `WebSearchAnswered` carries documents[{url, title, snippet, retrievedAt}].
4. Assistant **broadcasts** `ResearchClaimsProposed` (claims[{text, supportUrlIds[], confidence}]).
5. Claims without supportUrlIds are dropped or flagged `UnsupportedClaimDropped` — never silent fill.
6. Assistant **directs** `TableBuildAsked` → table artifact comparison.
7. `AssistantResponded` + `ChatArtifactProduced` (markdown brief + citations table).
8. Owner "save to SF" → approval optional → `SalesforceNoteCreateAsked` → `SalesforceNoteCreated` with citation footer.
9. Owner "remember" → `MemoryFactStoreAsked` → `MemoryFactStored` referencing brief synapseRef.

## Orleans / Core surface exercised
Serialized assistant multi-ask turn chain; DurableGrain journals; request context; outbox; module catalog; grain call filters; no need for transactions; optional stream for long research progress `ResearchProgressed`.

## Rich experience
Brief with footnotes; sortable citation table (title, url, used-by claims); claim confidence chips; export PDF action; open-source buttons.

## Failure / adversarial cases
- Model invents URL: only URLs present in `WebSearchAnswered` allowed in citations table (validator neuron or assistant policy).
- Search all fail: explicit `ResearchBriefFailed`; no fake brief.
- Stale cache: include retrievedAt; owner refresh forces new asks.
- Entity mix-up: disambiguation card before deep search.
- SF note size limits: truncate with link to chat artifact ref.

## Capability claim
Research output is a claim graph grounded in journaled search answers — auditably different from a chatbot paragraph with decorative fake links.
