# Flutter lane — production-source report

## What changed

- `core/lib/src/behavior_client.dart` — adopted Dart's null-aware map entries so optional feature
  fields remain omitted when null and current analysis is clean.
- `kit/lib/src/chat/kit_chat_builders.dart` — made the static unsupported-message subtree const.
- `kit/lib/src/components/chart/kit_chart.dart` — made the empty-series subtree const.
- `kit/lib/src/gallery/kit_gallery_screen.dart` — removed the obsolete `show-time` gallery sample,
  replacing it with a neutral `publish-summary` sample, and made static gallery widgets const.

## Source evidence

- Exact production scan for `show-time`, `ShowTime`, and `WantsTimeButton`: zero hits under the
  three Flutter `lib/` trees. The remaining `showTime: false` fields in shell control chat-message
  timestamp rendering and are unrelated to the deleted keyword action.
- Kit boundary scan found no imports or path dependencies on core or shell.
- The null-aware map entries were produced by the installed Dart analyzer fix for
  `use_null_aware_elements`; they preserve the prior conditional-entry behavior.
- Existing Flutter test files were not changed or executed, per the owner amendment.

## Adversarial review

- No wire alias, runtime route, package dependency, public API, or backend behavior changed.
- Const changes affect allocation only; the gallery identifier is offline sample data.
- No production keyword-god-switch vocabulary remains in the Flutter lane.

## Gate

`pwsh -NoProfile -File scripts/gate.ps1 -Flutter`:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
flutter analyze lib (core):  No issues found!
flutter analyze lib (kit):   No issues found!
flutter analyze lib (shell): No issues found!
GATE PASS
```

No automated test command ran.

## Conflicts & risks

- Context7 documentation lookup was attempted for the new Dart syntax but the configured service
  quota was exhausted. The installed Dart analyzer supplied and accepted the exact rewrite.

## Out of scope

- Flutter tests and the pre-existing `activateControl` test drift remain deferred with the entire
  automated-testing framework until final hardening.
