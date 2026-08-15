import 'dart:io';

Future<List<int>> readVoiceBytes(String path) => File(path).readAsBytes();

String joinVoicePath(String directory, String fileName) =>
    '$directory${Platform.pathSeparator}$fileName';

Future<void> deleteVoiceFile(String path) async {
  final file = File(path);
  if (await file.exists()) {
    await file.delete();
  }
}
