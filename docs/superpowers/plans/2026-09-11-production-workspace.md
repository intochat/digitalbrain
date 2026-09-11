# IntoCaht production workspace

Goal: implement the approved project-centered workspace in Flutter, then exercise the running application with Computer Use and repair observed issues.

Spec: `docs/design/workspace-options/README.md`, final Project-centered model section, and its HTML prototype.

Architecture: preserve the direct AG-UI conversational agent and authoritative table neurons. Use UiChat for presentation; project/conversation state owns explicit references, while artifact editors and window layout are separate. Settings is a full destination. Restore voice as transcription to an editable draft, without invoking the legacy turn orchestration.

Tech stack: existing Flutter ui, Flutter Chat UI, Microsoft Agent Framework, Orleans UI table neurons; Markdraw, shared preferences, and native file picking where needed.

Constraints: IntoCaht product name; no permanent feature rail; no fake live connections or sample data presented as real; preserve user changes. The approved existing checkout contains required table and design changes, so work continues here. No deployment or git history rewriting.

1. Implement persistent project, conversation, attachment and layout state; test isolation and reload.
2. Implement routed settings, actual ui gallery, agent selection and honest connection state.
3. Implement chat/work split, project navigation, scoped search, compact tool receipts and explicit multi-attachment references. Preserve stream EOF/session contract.
4. Implement table, diagram, image and brain editors plus focus/compare/window presentation and mobile workspace switching.
5. Add backend artifact tools and transcription-only endpoint, keeping web search available and table authority unchanged.
6. Analyze and test Flutter and relevant backend seams; build and launch the actual app.
7. Use Computer Use to simulate project/conversation/settings/editor operations and live agent tools; capture screenshots, fix failures and repeat relevant checks.

Rulings: user has approved the design and requested uninterrupted implementation. Settings/profile values are local preferences, not account-management claims. Graph scenarios are explicit drafts until real execution is supported; existing observed brain graph remains available separately. Do not silently connect external systems.
