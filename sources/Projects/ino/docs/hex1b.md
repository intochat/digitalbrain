# Hex1b — TUI framework for ino

## Version status

| | Version | Date |
|---|---|---|
| **ino pinned** | 0.1.0 | ~March 2026 |
| **Latest NuGet** | 0.127.0 | April 10, 2026 |
| **Gap** | 126 versions behind | |

Hex1b does NOT use semver 0.x.y — it uses monotonic 0.NNN. There is no "v0.2" milestone. The CLAUDE.md reference to "hex1b 0.2" is stale. The APIs ino was waiting for (interactive runtime, presentation adapters) already exist in the current release.

## Packages

- **Hex1b** — core TUI library (widgets, reconciliation, rendering)
- **Hex1b.McpServer** — MCP server for terminal session management (pinned in `.config/dotnet-tools.json`)
- **Hex1b.Tool** — CLI tool for terminal management

## Architecture (React-inspired)

Hex1b separates configuration (immutable `*Widget` records) from rendering (mutable `*Node` classes) via a reconciliation loop, similar to React's virtual DOM.

**ino's current usage:** `TimelineView.Build(vm)` returns a `VStackWidget` tree. Tests walk the tree via reflection. The TUI prints it to stdout as plain text. No interactive runtime — just one-shot tree construction.

## Interactive runtime — already available

The APIs ino needs are documented in hex1b's current version:

### Hex1bTerminalBuilder (entry point)

```csharp
var terminal = new Hex1bTerminalBuilder()
    .WithHeadlessPresentation(80, 24)    // for tests
    // OR .WithConsolePresentation()     // for real terminal
    // OR .With(new WebSocketPresentationAdapter(ws))  // for web
    .WithAppWorkload(BuildUI)
    .Build();
```

### Presentation adapters

| Adapter | Use case |
|---|---|
| `ConsolePresentationAdapter` | Real terminal (Windows/macOS/Linux) |
| `HeadlessPresentationAdapter` | Testing — in-memory buffer, no console I/O |
| `WebSocketPresentationAdapter` | Web — sends rendered frames over WebSocket |
| Custom `IHex1bTerminalPresentationAdapter` | Any surface |

### IHex1bTerminalPresentationAdapter interface

```csharp
public interface IHex1bTerminalPresentationAdapter
{
    void Initialize(Hex1bTerminal terminal);
    void Render(ReadOnlySpan<CellAttributes> buffer, int width, int height);
    void SetCursorPosition(int x, int y);
    void SetCursorVisible(bool visible);
    void SetCursorShape(CursorShape shape);
    (int Width, int Height) GetSize();
    void Dispose();
}
```

### WebSocketPresentationAdapter (documented pattern)

```csharp
public class WebSocketPresentationAdapter : IHex1bTerminalPresentationAdapter
{
    readonly WebSocket _socket;
    public WebSocketPresentationAdapter(WebSocket socket) => _socket = socket;

    public void Render(ReadOnlySpan<CellAttributes> buffer, int width, int height)
    {
        var data = SerializeBuffer(buffer, width, height);
        _socket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
    }
    // ... other methods
}
```

## What this means for ino

### Upgrade path (from 0.1.0 to latest)

1. Update `Directory.Packages.props`: `<PackageVersion Include="Hex1b" Version="0.127.0" />`
2. Fix breaking changes — the widget constructor API may have shifted (e.g. `VStackWidget(Children)` vs `VStackWidget { Children = ... }`)
3. Replace the plain-text stdout walker with the real `ConsolePresentationAdapter` for terminal output
4. Keep the widget-tree-walk tests (they test structure, not rendering)

### Three rendering surfaces from one widget tree

Once upgraded, ino can render the SAME `TimelineView.Build(vm)` widget tree to:

| Surface | Adapter | How |
|---|---|---|
| Windows/macOS terminal | `ConsolePresentationAdapter` | `ino-demo` uses this for real interactive TUI |
| Telegram mini app | `WebSocketPresentationAdapter` | Bot opens a WebSocket, mini app connects, adapter sends rendered frames |
| Tests | `HeadlessPresentationAdapter` | In-memory buffer, assert on cell contents |

### E2E testing with HeadlessPresentationAdapter

```csharp
// Build the TUI with a headless adapter so no console is needed
var adapter = new HeadlessPresentationAdapter(80, 24);
var terminal = new Hex1bTerminalBuilder()
    .WithPresentation(adapter)
    .WithAppWorkload(ctx => TimelineView.Build(vm))
    .Build();

// Read the rendered buffer and assert on cell contents
var buffer = adapter.GetBuffer();
// Assert cells contain expected text at expected positions
```

This replaces the current reflection-based widget-tree walker with proper rendering assertions — the test sees exactly what the user would see in a terminal.

## Open issues relevant to ino

| Issue | Title | Relevance |
|---|---|---|
| #170 | Windows nested terminal support via console scraping fallback | ino.windows integration |
| #164 | WindowPanel background bleeds through to overlay Drawer content | UI polish |
| #163 | Diagnostic shell workload loses content on terminal resize | resize handling |
| #154 | API Design: Builder vs Context naming | API stability signal |
| #165 | Editor Widget — extensible document editing | future ino editor surface |
| #240 | MarkdownWidget: syntax highlighting for fenced code blocks | rendering markdown in TUI |

## Action items

1. **Upgrade hex1b from 0.1.0 to latest** — the biggest single unblock. Do it in a dedicated branch because breaking changes will touch TimelineView, all tests, and the demo.
2. **Replace stdout walker with ConsolePresentationAdapter** — ino-demo becomes a real interactive TUI instead of one-shot print.
3. **Add WebSocket adapter to Telegram bot** — mini app connects via WebSocket, receives rendered terminal frames, displays in `<canvas>` or character-grid divs. This makes the mini app a REAL terminal emulator, not a text dump.
4. **Add HeadlessPresentationAdapter to E2E tests** — replace the reflection-based widget-tree walker with proper buffer assertions.
5. **Track hex1b API stability** — hex1b has 44 open issues and no stability guarantee. Pin to a specific version in `Directory.Packages.props` and upgrade deliberately.

## Project links

- NuGet: https://www.nuget.org/packages/Hex1b
- GitHub: https://github.com/mitchdenny/hex1b
- Issues: https://github.com/mitchdenny/hex1b/issues
