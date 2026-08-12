import 'process_environment_stub.dart'
    if (dart.library.io) 'process_environment_io.dart' as process_env;
import 'runtime_surface_io.dart'
    if (dart.library.html) 'runtime_surface_web.dart' as surface;

abstract final class DigitalBrainHostEnv {
  static const uiBaseVariable = 'DIGITALBRAIN_UI_BASE';
  static const shellVariable = 'DIGITALBRAIN_SHELL';
  static const defaultShellName = 'desk';
  // Local conversation name (Seam 5). Prefer DIGITALBRAIN_CONVERSATION; DIGITALBRAIN_CHAT remains alias.
  static const conversationVariable = 'DIGITALBRAIN_CONVERSATION';
  static const chatVariable = 'DIGITALBRAIN_CHAT';
  static const defaultChatName = 'main';
  static const usernameVariable = 'DIGITALBRAIN_USERNAME';
  static const passwordVariable = 'DIGITALBRAIN_PASSWORD';
  static const defaultUsername = 'owner';
  static const defaultPassword = 'ownerowner';

  static const hostProcessVariables = {
    uiBaseVariable,
    shellVariable,
    chatVariable,
    usernameVariable,
    passwordVariable,
  };

  static String resolveUiBaseRaw({
    String fromDefine = const String.fromEnvironment(uiBaseVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? process_env.readProcessEnvironment();
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
    if (raw.isNotEmpty) {
      return Uri.parse(raw);
    }

    // Deployed shell served from digitalbrain-ui (same origin) has no process env.
    final sameOrigin = surface.sameOriginUiBase();
    if (sameOrigin != null) {
      return sameOrigin;
    }

    throw StateError(
      '$uiBaseVariable is required (AppHost WithFlutterHost injects it).',
    );
  }

  static String resolveShell({
    String fromDefine = const String.fromEnvironment(shellVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? process_env.readProcessEnvironment();
    final raw = process[shellVariable] ?? '';
    if (raw.isEmpty) {
      return defaultShellName;
    }
    return raw;
  }

  static String resolveChat({
    String fromDefine = const String.fromEnvironment(conversationVariable),
    String fromChatDefine = const String.fromEnvironment(chatVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    if (fromChatDefine.isNotEmpty) {
      return fromChatDefine;
    }
    final process = processEnvironment ?? process_env.readProcessEnvironment();
    final raw = process[conversationVariable] ?? process[chatVariable] ?? '';
    if (raw.isEmpty) {
      return defaultChatName;
    }
    return raw;
  }

  static String resolveConversation({
    String fromDefine = const String.fromEnvironment(conversationVariable),
    String fromChatDefine = const String.fromEnvironment(chatVariable),
    Map<String, String>? processEnvironment,
  }) =>
      resolveChat(
        fromDefine: fromDefine,
        fromChatDefine: fromChatDefine,
        processEnvironment: processEnvironment,
      );

  static String resolveUsername({
    String fromDefine = const String.fromEnvironment(usernameVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? process_env.readProcessEnvironment();
    final raw = process[usernameVariable] ?? '';
    return raw.isEmpty ? defaultUsername : raw;
  }

  static String resolvePassword({
    String fromDefine = const String.fromEnvironment(passwordVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }
    final process = processEnvironment ?? process_env.readProcessEnvironment();
    final raw = process[passwordVariable] ?? '';
    return raw.isEmpty ? defaultPassword : raw;
  }
}
