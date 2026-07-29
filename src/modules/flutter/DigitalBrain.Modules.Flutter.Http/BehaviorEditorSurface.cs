using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;

namespace DigitalBrain.UI;

internal static class BehaviorEditorSurface
{
    public static IEndpointRouteBuilder MapBehaviorEditorSurface(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiHttpContract.BehaviorEditorSurfacePath,
            static async Task<IResult> (
                string? behaviorId,
                string? shell,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var id = string.IsNullOrWhiteSpace(behaviorId)
                    ? UiHttpContract.AccountEnrichmentBehaviorId
                    : behaviorId.Trim();
                var shellName = string.IsNullOrWhiteSpace(shell) ? "desk" : shell.Trim();

                await brain.Get<IShell>(shellName).Open(
                    new OpenScene(
                        CommandId.New(),
                        UiHttpContract.BehaviorEditorSceneKey,
                        UiHttpContract.BehaviorEditorSceneTitle));

                return Results.Content(
                    RenderHtml(id),
                    "text/html; charset=utf-8");
            });

        return endpoints;
    }

    private static string RenderHtml(string behaviorId)
    {
        var encodedId = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(behaviorId);
        var html = new StringBuilder();
        html.Append(
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>DigitalBrain behavior editor</title>
              <link rel="stylesheet" href="/monaco/editor.main.css" />
              <style>
                :root {
                  color-scheme: dark;
                  --bg: #141720;
                  --panel: #1b1f2a;
                  --line: #292e3b;
                  --text: #e9ebf2;
                  --muted: #969caf;
                  --signal: #e09261;
                  --ok: #65c5a0;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  font: 14px/1.4 "Segoe UI", system-ui, sans-serif;
                  background: var(--bg);
                  color: var(--text);
                }
                header {
                  display: flex;
                  gap: 12px;
                  align-items: center;
                  justify-content: space-between;
                  padding: 12px 16px;
                  border-bottom: 1px solid var(--line);
                  background: var(--panel);
                }
                header h1 {
                  margin: 0;
                  font-size: 15px;
                  font-weight: 600;
                }
                header .meta { color: var(--muted); font-size: 12px; }
                .actions { display: flex; gap: 8px; align-items: center; }
                button {
                  border: 1px solid var(--line);
                  background: #11141b;
                  color: var(--text);
                  border-radius: 6px;
                  padding: 7px 12px;
                  cursor: pointer;
                }
                button.primary {
                  border-color: color-mix(in srgb, var(--signal) 50%, var(--line));
                  color: var(--signal);
                }
                button:disabled { opacity: 0.45; cursor: default; }
                #status {
                  padding: 8px 16px;
                  border-bottom: 1px solid var(--line);
                  color: var(--muted);
                  font-family: Consolas, "Cascadia Mono", monospace;
                  font-size: 12px;
                }
                #status.ok { color: var(--ok); }
                #status.fail { color: var(--signal); }
                main {
                  display: grid;
                  grid-template-columns: 1fr 1fr;
                  gap: 1px;
                  background: var(--line);
                  height: calc(100vh - 96px);
                }
                section {
                  display: flex;
                  flex-direction: column;
                  background: var(--bg);
                  min-height: 0;
                }
                section h2 {
                  margin: 0;
                  padding: 8px 12px;
                  font-size: 12px;
                  letter-spacing: 0.04em;
                  text-transform: uppercase;
                  color: var(--muted);
                  border-bottom: 1px solid var(--line);
                }
                .editor { flex: 1; min-height: 0; }
              </style>
            </head>
            <body>
              <header>
                <div>
                  <h1>Behavior editor</h1>
                  <div class="meta" id="behavior-id"></div>
                </div>
                <div class="actions">
                  <button id="save" class="primary">Save proposal</button>
                  <button id="run-tests">Run tests</button>
                  <button id="approve">Approve</button>
                </div>
              </header>
              <div id="status">Loading…</div>
              <main>
                <section>
                  <h2>program.cs</h2>
                  <div id="program" class="editor"></div>
                </section>
                <section>
                  <h2>install.feature</h2>
                  <div id="feature" class="editor"></div>
                </section>
              </main>
              <script>window.DIGITALBRAIN_BEHAVIOR_ID = "
            """);
        html.Append(encodedId);
        html.Append(
            """
            ";</script>
              <script src="/monaco/loader.js"></script>
              <script src="/monaco/behavior-editor.js"></script>
            </body>
            </html>
            """);
        return html.ToString();
    }
}
