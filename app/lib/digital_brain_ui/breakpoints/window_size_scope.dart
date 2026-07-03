import 'package:flutter/widgets.dart';

import 'window_size.dart';

WindowSize _windowSizeFromWidth(double width) {
  if (width < 600) return WindowSize.compact;
  if (width < 840) return WindowSize.medium;
  if (width < 1200) return WindowSize.expanded;
  if (width < 1600) return WindowSize.large;
  return WindowSize.xLarge;
}

/// Publishes the current [WindowSize] computed from [MediaQuery.sizeOf]'s width.
/// Cheap to read; consumers call [WindowSize.of(context)] (extension below).
class WindowSizeScope extends StatelessWidget {
  const WindowSizeScope({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    return _WindowSizeInherited(
      size: _windowSizeFromWidth(width),
      child: child,
    );
  }
}

class _WindowSizeInherited extends InheritedWidget {
  const _WindowSizeInherited({required this.size, required super.child});

  final WindowSize size;

  @override
  bool updateShouldNotify(_WindowSizeInherited old) => old.size != size;
}

extension WindowSizeContext on WindowSize {
  static WindowSize of(BuildContext context) {
    final scope = context
        .dependOnInheritedWidgetOfExactType<_WindowSizeInherited>();
    assert(
      scope != null,
      'WindowSize.of() called outside a WindowSizeScope; wrap DigitalBrainApp.',
    );
    return scope!.size;
  }
}
