import 'dart:io' show Platform;

abstract final class DigitalBrainHostEnv {
  static const uiBaseVariable = 'DIGITALBRAIN_UI_BASE';
  static const shellVariable = 'DIGITALBRAIN_SHELL';
  static const defaultShellName = 'desk';

  static const hostProcessVariables = {
    uiBaseVariable,
    shellVariable,
  };

  static String resolveUiBaseRaw({
    String fromDefine = const String.fromEnvironment(uiBaseVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? Platform.environment;
    return process[uiBaseVariable] ?? '';
  }

  static Uri requireUiBaseUri({
    String fromDefine = const String.fromEnvironment(uiBaseVariable),
    Map<String, String>? processEnvironment,
  }) {
    final raw = resolveUiBaseRaw(
      fromDefine: fromDefine,
      processEnvironment: processEnvironment,
    );
    if (raw.isEmpty) {
      throw StateError(
        '$uiBaseVariable is required (AppHost WithFlutterHost injects it).',
      );
    }
    return Uri.parse(raw);
  }

  static String resolveShell({
    String fromDefine = const String.fromEnvironment(shellVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? Platform.environment;
    final raw = process[shellVariable] ?? '';
    if (raw.isEmpty) {
      return defaultShellName;
    }
    return raw;
  }
}
