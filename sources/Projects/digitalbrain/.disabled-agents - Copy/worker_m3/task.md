# Task Brief: Worker Milestone 3 (Core Legacy Deletions)
- Working Directory: E:\digitalbrain\.agents\worker_m3\
- Role: Perform Core Legacy Deletions (Milestone 3) tasks in the implementation plan.
  1. Confirm nothing imports `brain_scene_screen.dart`, `constellation/`, `constructor_editor_home_page.dart`, `neuron_constructor_view.dart`, `liquid_glass_3d_brain.dart`.
  2. Delete:
     - `UI/flutter/lib/features/brain/brain_scene_screen.dart`
     - `UI/flutter/lib/features/constellation/` (entire directory)
     - `UI/flutter/lib/features/home/constructor_editor_home_page.dart`
     - `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`
     - `UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart`
  3. Ensure the small models `visual_constructor_models.dart` and `visual_constructor_state.dart` are kept intact in `UI/flutter/lib/features/neuron_constructor/`.
  4. Run `flutter analyze` to check for any imports that are broken by these deletions (unused elements or imports are okay, but no compilation errors).
  5. Write your handoff report to `E:\digitalbrain\.agents\worker_m3\handoff.md`.
