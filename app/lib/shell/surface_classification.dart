part of 'forui_app_shell.dart';

/// Surface kinds that should immediately become the visible shell body the moment
/// they arrive over the home-feed stream, mirroring the existing gallery auto-switch
/// a few lines below. Returns the `_selectedTarget` to switch to, or null if [kind]
/// shouldn't trigger an auto-switch. A plain top-level function (not a method) so it's
/// unit-testable without pumping the full widget tree or mocking the gRPC connection.
String? autoSwitchTargetForKind(String kind) {
  if (kind == 'pack-config-form') return kind;
  return null;
}

enum SurfaceDisposition { shell, chat, content, toast, ignore }

String surfaceKindOf(Map<String, Object?> data) =>
    (data['kind'] ?? data['surfaceKind'] ?? '').toString();

SurfaceDisposition classifySurface(Map<String, Object?> data) {
  final kind = surfaceKindOf(data).toLowerCase();
  if (kind == 'toast' || kind == 'notification' || kind.contains('toast')) {
    return SurfaceDisposition.toast;
  }
  if (isShellSurface(data)) return SurfaceDisposition.shell;
  if (isChatSurface(data)) return SurfaceDisposition.chat;
  if (kind.isNotEmpty) return SurfaceDisposition.content;
  return SurfaceDisposition.ignore;
}

bool isChatSurface(Map<String, Object?> data) =>
    data['role'] == 'assistant' && data['tree'] is Map;

bool isShellSurface(Map<String, Object?> data) {
  final kind = surfaceKindOf(data).toLowerCase();
  if (kind == 'app-shell' || kind.contains('shell')) return true;
  if (data['activeContent'] != null) return true;

  final treeNode = data['tree'];
  if (treeNode is! Map) return false;
  if (treeNode['activeContent'] != null) return true;

  final props = treeNode['Props'];
  if (props is Map && props['activeContent'] != null) return true;

  final type =
      treeNode['Type']?.toString().toLowerCase() ??
      treeNode['type']?.toString().toLowerCase() ??
      '';
  return type.contains('scaffold') || type == 'app-shell';
}

bool shellChatIsSelected(String location, String? selectedTarget) {
  final target = (selectedTarget ?? '').trim().toLowerCase();
  return location == '/chat' ||
      target == 'chat' ||
      target == '/chat' ||
      target.contains('ino');
}
