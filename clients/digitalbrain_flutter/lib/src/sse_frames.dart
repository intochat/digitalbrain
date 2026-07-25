import 'dart:convert';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';

final class SseSceneOpenedParser {
  String? _dataLine;

  Iterable<SceneOpenedEvent> addLine(String line) sync* {
    if (line.startsWith('data:')) {
      _dataLine = line.substring('data:'.length).trim();
      return;
    }

    if (line.isEmpty && _dataLine != null) {
      final payload = _dataLine!;
      _dataLine = null;
      final event = _decode(payload);
      if (event != null) {
        yield event;
      }
    }
  }

  Iterable<SceneOpenedEvent> flush() sync* {
    if (_dataLine == null) {
      return;
    }
    final event = _decode(_dataLine!);
    _dataLine = null;
    if (event != null) {
      yield event;
    }
  }

  static SceneOpenedEvent? _decode(String payload) {
    final decoded = jsonDecode(payload);
    if (decoded is! Map<String, dynamic>) {
      return null;
    }
    return SceneOpenedEvent.fromJson(decoded.cast<String, Object?>());
  }
}
