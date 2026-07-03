/// Public barrel for the future `digital-brain-ui` pub package.
/// When the package is extracted, this file becomes lib/digital_brain_ui.dart
/// at the new repo root with no other code changes.
library;

export 'breakpoints/window_size.dart';
export 'breakpoints/window_size_scope.dart';
export 'breakpoints/input_mode.dart';
export 'density/adaptive_density.dart';

export 'adaptive/adaptive_dialog.dart';
export 'adaptive/adaptive_sheet.dart';
export 'adaptive/adaptive_side_sheet.dart';
export 'adaptive/adaptive_surface.dart';

// glass/*, rfw/*, debug/* added by later tasks.
export 'debug/debug_brain_stats.dart';
export 'glass/glass_material.dart';
export 'glass/liquid_glass_surface.dart';
export 'effects/brain_scene_effects.dart';
export 'effects/effects_pulse.dart';

export 'glow/glow_icon.dart';
export 'glow/glow_icon_spec.dart';
export 'glow/glow_painter.dart';
