# Seam 6 — UI voice bind honesty

**Tip base:** `39e7d81b` · Flutter bind SHA lineage `ae4b43a3`.

## UI done (tip-true)
| Surface | Path | Domain owner |
|---|---|---|
| Mic upload | `POST /conversations/{name}/voice` via `DigitalBrainUiClient.streamVoice` | `IConversation.Send(SendConversationMessage)` after Whisper |
| Text send | `conversation.send` owner command | Conversations |
| Turn SSE | `GET /conversations/{name}/events` | journal projection (tip may still key IChat instance name — Modules strangle) |

## Explicit non-use
- Flutter does **not** call `/chats/{name}/voice` (`MapChatVoice` / IChat-only).
- Shell mic → `onStreamVoice` → `streamVoice` only.

## Residual (not UI)
- Host still maps `MapChatVoice` for `/chats/…/voice` — Kernel/Modules to retire when dual-map ends.
- Responded watch may still read tip `IChat` outgoing journal under the same principal-partitioned instance name (ConversationNeuron strangle).

## Env
- Prefer `DIGITALBRAIN_CONVERSATION`; `DIGITALBRAIN_CHAT` remains alias for local name.
