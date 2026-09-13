import 'dart:io';

Future<List<int>> readVoiceBytes(String path) => File(path).readAsBytes();

String joinVoicePath(String directory, String fileName) =>
    '$directory${Platform.pathSeparator}$fileName';

Future<void> deleteVoiceFile(String path) async {
  for (var attempt = 0; ; attempt++) {
    try {
      await File(path).delete();
      return;
    } on PathNotFoundException {
      // The recorder or another cleanup already removed this capture.
      return;
    } on FileSystemException catch (error) {
      // Windows can report access denied while another handle's deletion is
      // pending. Retry briefly, but retain real permission and other failures.
      if (!Platform.isWindows || ![5, 32].contains(error.osError?.errorCode)) {
        rethrow;
      }
      final type = await FileSystemEntity.type(path, followLinks: false);
      if (type == FileSystemEntityType.notFound) return;
      if (type != FileSystemEntityType.file || attempt >= 3) rethrow;
      await Future<void>.delayed(Duration(milliseconds: 20 * (1 << attempt)));
    }
  }
}
