import 'package:workspace/surface/feed_cursor_store.dart';

class MemoryFeedCursorStore implements FeedCursorStore {
  int? _cursor;

  @override
  int? read() => _cursor;

  @override
  void write(int cursor) {
    _cursor = cursor;
  }
}
