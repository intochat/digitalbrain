# Task Brief: Worker Milestone 2 (LivingCanvasScreen & Routing)
- Working Directory: E:\digitalbrain\.agents\worker_m2\
- Role: Implement LivingCanvasScreen & Routing (Milestone 2) tasks in the implementation plan.
  1. Create `UI/flutter/lib/features/canvas/living_canvas_screen.dart` exactly as specified in E:\digitalbrain\docs\superpowers\plans\2026-05-29-flutter-cut-living-canvas-s1.md.
  2. Implement GoRouter changes in `UI/flutter/lib/router.dart`:
     - Map `/` to `LivingCanvasScreen` as the root screen.
     - Remove `/constellation` and `/brain/:brainId` routes.
     - Remove dead imports and `BrainScenePlaceholder`.
  3. Verify with `flutter analyze` that everything compiles perfectly.
  4. Write your completion handoff report to `E:\digitalbrain\.agents\worker_m2\handoff.md`.
