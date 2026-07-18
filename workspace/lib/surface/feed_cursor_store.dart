abstract class FeedCursorStore {
  int? read();
  void write(int cursor);
}

class MemoryFeedCursorStore implements FeedCursorStore {
  int? _cursor;

  @override
  int? read() => _cursor;

  @override
  void write(int cursor) {
    _cursor = cursor;
  }
}
