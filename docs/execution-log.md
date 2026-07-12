# DigitalBrain Execution Log — shape-v3

Baseline (2026-07-12 on v2): C# src 28,508 · C# test 13,644 · Dart lib 22,467 · Dart test 5,439 · Total 70,058. Target ≤42,035 (−40%).

P0.1 | deb06c1 | delta: 0 (baseline) | branch shape-v3 created from v2; dotnet build green (0 errors); flutter analyze: 1 info non-error; flutter test: all green (exit 0); dotnet test: 366 passed, 5 FAILED (pre-existing...); Log initialized... (final P0 hash deb06c1 after amds).
P1.1 | 7150198 | delta: ~ -17 C# src (25550->25533); total ~62618 | Verified git ls-files empty for ghosts; deleted hosts/DigitalBrain.Telegram.Transport, integrations/DigitalBrain.Telegram, tests/DigitalBrain.Telegram.Tests, tools/DigitalBrain.RuntimeMigration (bin/obj only); removed empty /tools/ Folder from Brain.slnx. dotnet build green, dotnet test green (0 failed, 371+ passed). Acceptance: no Telegram/RuntimeMigration strings or slnx entries left. Used aspire stop + MCP stops to release DLL locks (reality: live AppHost from prior run). P1.1 commit 7150198.
P1.2 | 3618233 | delta: 0 (empty dirs, 0 dart removed) | Verified 0 dart in canvas/experience/spike; deleted the 3 empty feature dirs. dotnet build green; flutter analyze (1 info only); flutter test green (exit 0); dotnet test green (0 fails). P1.2 commit 3618233.
P1.3 | 9a3bd16 | delta: -~1k (911+106, total 61696) | Rewired main.dart (removed SURFACE_DEMO import + if, now always router/RuntimeShell /chat); deleted app/lib/features/surface_demo/ and app/test/features/; build+flutter+test green; no 'surface_demo' string in app/. Accept met. P1.3 commit 9a3bd16.
P1.9 | 649655b | delta: small (2 fields removed) | Removed CommissionRate and Price (legacy economics) from NeuroPack.cs. No other .cs references or test serializations found. dotnet build + full test green (0 fails). P1.9 commit 649655b.

DISCREPANCY (P1.4): Plan verify for features/live requires no external references in app/lib (grep excluding the dir itself must be empty). Reality: references still exist in rfw_host/:
- app/lib/rfw_host/digitalbrain_rfw_library.dart imports features/live/graph/domain_palette.dart
- app/lib/rfw_host/synapse_stream_scope.dart imports features/live/graph/cluster_layout.dart
Per rule 5: logged, skipping deletion of live/ for now. Will re-evaluate after P1.14/P1.15 RFW pruning or when those callers are removed. Continuing to next verifiable task.

DISCREPANCY (P1.5): Plan verify for features/brain requires the grep outside the dir to be empty. Found:
- app/lib/rfw_host/digitalbrain_rfw_library.dart:31 imports features/brain/voice_input.dart
Per rule 5: logged, skipping. Same RFW registration bloat issue.

DISCREPANCY (P1.6): Plan states neuron_vector_logo "Used only by deleted `features/live/graph/brain_painter.dart` and `ui_kit/ui_button.dart`". Verify first via grep for "neuron_vector_logo|NeuronVectorLogo|LogoPainter" excluding the widget file itself:
- app/lib/ui_kit/ui_button.dart:5 import + NeuronVectorLogo widget usage in _ButtonContent (for iconName)
- app/lib/features/live/graph/brain_painter.dart:5 import + NeuronVectorLogo.resolveCategory(entry.key) + LogoPainter(...) direct ctor + .paint() in _drawNodes (when replaceSpheresWithIcons)
- app/test/ui_kit/ui_kit_widgets_test.dart:11 import + expect(find.byType(NeuronVectorLogo))
Reality contradicts plan premise: live/brain_painter.dart is NOT deleted (see P1.4 discrepancy; it is still imported/referenced from rfw_host/*). brain_painter depends on BOTH the widget's static resolveCategory AND the sibling LogoPainter class (not just widget). ui_button usage is real (for RFW-driven buttons). No "deleted live" to justify. Per instructions: verification contradicts, logged, SKIP P1.6 entirely. No code changes made for this task. Continue to next verifiable task. (Codegraph + grep confirmed blast radius includes live/ + test.)

DISCREPANCY (P1.7): Move of the 5 rfw_host-imported files (+ ino_source_subscription required by relative import inside ino_editor_bus.dart) to runtime/buses/ completed; imports in digitalbrain_rfw_library.dart updated; ino_editor/ dir deleted (all 6 files moved, no orphaned tests). flutter analyze (1 pre-existing info) + flutter test all green; dotnet build+test green. HOWEVER, plan states "After this, `app/lib/features/` must be EMPTY — delete the directory." Reality: after ino_editor removal, features/ still contains live/ and brain/ (see logged P1.4 + P1.5 discrepancies; both still imported by rfw_host/digitalbrain_rfw_library.dart + live code). Deleting features/ would break rfw_host and live/brain functionality which we are required to preserve per prior skips. Per rule: logged discrepancy; did NOT delete features/ dir. Core task (move+cleanup of ino_editor) complete and verified. Will re-eval features/ deletion after P1.14/P1.15 or when rfw_host no longer refs live/brain. P1.7 work committed.

P1.8 | 61856e8 | delta: pkgs+config removed | Dead pkgs (Qdrant, Spectre*, Redis pers, Stripe) + xai param + dashboard option/wiring deleted after verify no .cs usage; build+test green.
P1.10 | 7ebfe29 | delta: -~1.2k (neurons+tests) | Deleted 5 workbench neurons + refs in Synapse/Generated/SelfEvolution + db test + neuron tests + pruned tied sqlite upload; synapses thinned (models kept live); tests green.
P1.11 | 7e0c87b | delta: -278 | Deleted UiKitGallery + DbSchemaGraphMapper + test (post P1.10 db kill); audit no exclusive gallery dep; green.
P1.12 | c056036 | delta: -~755 (meta) | Deleted skills-lock + tmp summary txt; tmp/ gitignore; AGENTS.md -> 3-line CLAUDE.md pointer; green.
P1.13 | c056036 | LOC: C# src 24404 · C# test 11682 · Dart lib 19525 · Dart test 4788 · Total 60399 (baseline 70058; -~9.7k Phase1A) | LOC accounting after Tier A (skips logged 1.4-1.6); all green. Tier A checkpoint. Now Tier B.
