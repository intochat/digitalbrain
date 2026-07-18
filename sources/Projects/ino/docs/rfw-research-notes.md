# RFW (Remote Flutter Widgets) — Research notes for Slice 4

**Library:** `package:rfw` from `flutter/packages` monorepo
**Latest stable on pub.dev (as of 2026-05-02):** `1.1.3`
**Verified against source:** `flutter/packages` `main` branch (`packages/rfw/lib/src/dart/text.dart`)

The four blocking research questions for Slice 4. All answered green; no
showstoppers.

---

## R1 — Security model: declarative or executable?

**Answer: Strictly declarative. No code execution. Safe for v0.1.**

Quoting the package documentation:

> "Since remote widget libraries are declarative and not code, they cannot
> represent executable closures."

> "With RFW you can use those widgets, but it doesn't let you _create_
> those widgets."

The DSL evaluates to a tree of widget constructors with literal-or-data-bound
arguments. Event handlers (`event "name" { args }`) are AST nodes that the
host catches via `RemoteWidget.onEvent` — they cannot run host-defined code
directly; the host decides what each event name does.

**Implication for ino:** v0.1 is safe even though every domain silo can
ship arbitrary DSL — they can't break out of the declarative box. The
post-v0.1 marketplace plan still needs review when third-party domains land
(curating which `LocalWidgetLibrary` builders are exposed, locking down
event-name namespaces), but that is **not** a sandbox/escape concern; it is
a "what widgets do we expose to untrusted DSL authors" concern. Defer to
post-v0.1.

**Decision: PROCEED.**

---

## R2 — Parser CRLF tolerance

**Answer: The parser still rejects `\r`. Server-side strip is required.**

`text.dart` tokenizer accepts only `0x20` (space) and `0x0A` (LF) as
whitespace:

```dart
case 0x0A: // U+000A LINE FEED (LF)
case 0x20: // U+0020 SPACE character
  start = index;
```

A `\r` (0x0D) falls through to the default branch and triggers
`throw ParserException('Unexpected character …')`. The grammar comment
confirms:

> `WS ::= ( <U+0020> | <U+000A> | "//" comment* <U+000A or EOF> | "/*" blockcomment "*/" )`

**Implication for ino:** every server-emitted DSL byte buffer must be
stripped of `\r` before `ChatResponse.rfw_description` goes on the wire.
Sub-commit 4A Task 4 bakes a `StripCr` helper into `InoGrpcService`.

**Decision: PROCEED with CRLF strip on the wire-write path.**

---

## R3 — Two-way event callbacks

**Answer: `event "name" { args }` AST nodes; host listens via `RemoteWidget.onEvent`.**

DSL syntax (from `_readEventHandler`):

```dart
_expectIdentifier('event');
final String name = _readString();
final DynamicMap args = _readMap(extended: false);
return _withSourceRange(EventHandler(name, args), start);
```

So the canonical form is:

```rfwtxt
onPressed: event "digit" { arguments: [7] }
onTap:     event 'shop.productSelect' { name: args.product.name }
```

(Both single-quoted and double-quoted string literals work — `_readString`
accepts either.)

Host-side, the `RemoteWidget` exposes an `onEvent` callback parameter:

```dart
RemoteWidget(
  runtime: runtime,
  data: data,
  widget: const FullyQualifiedWidgetName(...),
  onEvent: (String name, DynamicMap arguments) {
    // dispatch — name is 'flight.selected', arguments is { flightId: 'FL-001' }
  },
)
```

**There is no `Runtime.eventStream`** — the plan template referenced one,
but the canonical API is the `RemoteWidget.onEvent` callback. Sub-commit 4C
Task 15 (`event_dispatcher.dart`) and Task 16 (`_RfwBubble`) need to wire
the dispatcher through that callback rather than subscribing to a runtime
stream.

**Decision: PROCEED. Use `event 'name' { args }` in card builders, and
wire the dispatcher into `RemoteWidget.onEvent` in the bubble.**

---

## R4 — Streaming / incremental updates

**Answer: Wholesale replacement is fine for our use-case.**

Each chat bubble carrying RFW gets its own `RemoteWidget` rooted at a
fresh `Runtime + DynamicContent`. When a new `ChatResponse` arrives with
fresh `rfw_description` / `rfw_data`, a new `_RfwBubble` is created and
its own `initState` parses the new DSL — no incremental delta required.

`DynamicContent.update('data', …)` would let us mutate state in place if
we wanted animated transitions inside a single bubble, but that is out of
scope for v0.1.

**Decision: PROCEED. Each plan step emits a fresh `RfwPayload`; a fresh
bubble renders it. No incremental update plumbing needed.**

---

## Summary — go/no-go

| Question | Status | Action |
|---|---|---|
| R1 — security | Green | Proceed; defer marketplace concerns post-v0.1 |
| R2 — CRLF | Server-side strip required | `StripCr` helper in 4A Task 4 |
| R3 — event syntax | Confirmed `event "name" { args }`; host uses `RemoteWidget.onEvent` | Update 4C dispatcher to use `onEvent` callback, not `runtime.events` |
| R4 — incremental | Wholesale replacement fine | No change to plan |

**Decision: PROCEED to Sub-commit 4A.**
