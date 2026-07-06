part of 'forui_app_shell.dart';

@visibleForTesting
const shellDropOverlayKey = Key('shell-drop-overlay');

@visibleForTesting
String uploadFileName(XFile file) {
  final explicitName = file.name.trim();
  if (explicitName.isNotEmpty) return explicitName;

  final path = file.path.trim();
  if (path.isEmpty) return 'upload';

  final parts = path.split(RegExp(r'[\\/]'));
  final fallback = parts.isEmpty ? path : parts.last;
  return fallback.trim().isEmpty ? 'upload' : fallback;
}

@visibleForTesting
Future<void> ingestDroppedFilesForShell(
  Iterable<XFile> droppedFiles,
  Future<void> Function(List<XFile> files) ingest,
) async {
  final files = droppedFiles
      .where((file) => file is! DropItemDirectory)
      .toList(growable: false);
  if (files.isEmpty) return;
  await ingest(files);
}
