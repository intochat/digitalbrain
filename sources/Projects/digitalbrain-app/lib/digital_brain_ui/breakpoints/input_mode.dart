import 'package:flutter/foundation.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/widgets.dart';

/// Input modality currently in use.
enum InputMode { touch, pointer, stylus }

/// Publishes the current [InputMode]. Resolution priority:
///   1. Most-recent pointer event kind (touch, mouse, stylus) if observed,
///   2. else platform-conventional default
///      (iOS/Android/Fuchsia = touch, desktop = pointer).
class InputModeScope extends StatefulWidget {
  const InputModeScope({required this.child, super.key});

  final Widget child;

  @override
  State<InputModeScope> createState() => _InputModeScopeState();
}

class _InputModeScopeState extends State<InputModeScope> {
  late InputMode _mode = _platformDefault();

  static InputMode _platformDefault() {
    switch (defaultTargetPlatform) {
      case TargetPlatform.android:
      case TargetPlatform.iOS:
      case TargetPlatform.fuchsia:
        return InputMode.touch;
      case TargetPlatform.linux:
      case TargetPlatform.macOS:
      case TargetPlatform.windows:
        return InputMode.pointer;
    }
  }

  void _onPointer(PointerEvent event) {
    final next = switch (event.kind) {
      PointerDeviceKind.touch => InputMode.touch,
      PointerDeviceKind.stylus ||
      PointerDeviceKind.invertedStylus => InputMode.stylus,
      PointerDeviceKind.mouse ||
      PointerDeviceKind.trackpad ||
      PointerDeviceKind.unknown => InputMode.pointer,
    };
    if (next != _mode) setState(() => _mode = next);
  }

  @override
  Widget build(BuildContext context) {
    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: _onPointer,
      onPointerHover: _onPointer,
      child: _InputModeInherited(mode: _mode, child: widget.child),
    );
  }
}

class _InputModeInherited extends InheritedWidget {
  const _InputModeInherited({required this.mode, required super.child});

  final InputMode mode;

  @override
  bool updateShouldNotify(_InputModeInherited old) => old.mode != mode;
}

extension InputModeContext on InputMode {
  static InputMode of(BuildContext context) {
    final scope = context
        .dependOnInheritedWidgetOfExactType<_InputModeInherited>();
    assert(
      scope != null,
      'InputMode.of() called outside an InputModeScope; wrap DigitalBrainApp.',
    );
    return scope!.mode;
  }
}
