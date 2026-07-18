import 'dart:io';

import 'feed_cursor_store.dart';

class FileFeedCursorStore implements FeedCursorStore {
  FileFeedCursorStore(this.path);

  final String path;

  static String defaultPath() {
    final home =
        Platform.environment['LOCALAPPDATA'] ??
        Platform.environment['HOME'] ??
        Directory.systemTemp.path;
    return '$home${Platform.pathSeparator}digitalbrain${Platform.pathSeparator}feed_cursor';
  }

  @override
  int? read() {
    final file = File(path);
    if (!file.existsSync()) {
      return null;
    }
    final text = file.readAsStringSync().trim();
    if (text.isEmpty) {
      return null;
    }
    return int.tryParse(text);
  }

  @override
  void write(int cursor) {
    final file = File(path);
    final parent = file.parent;
    if (!parent.existsSync()) {
      parent.createSync(recursive: true);
    }
    final temp = File(
      '${file.path}.tmp.$pid.${DateTime.now().microsecondsSinceEpoch}',
    );
    temp.writeAsStringSync('$cursor\n', flush: true);
    if (file.existsSync()) {
      file.deleteSync();
    }
    temp.renameSync(file.path);
  }
}
