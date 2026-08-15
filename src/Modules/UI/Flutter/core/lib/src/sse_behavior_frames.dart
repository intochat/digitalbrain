import 'dart:convert';

import 'behavior_models.dart';

final class SseBehaviorParser {
  String? _dataLine;
  String? _eventName;

  Iterable<BehaviorEvent> addLine(String line) sync* {
    if (line.startsWith(':')) {
      return;
    }

    if (line.startsWith('event:')) {
      _eventName = line.substring('event:'.length).trim();
      return;
    }

    if (line.startsWith('data:')) {
      _dataLine = line.substring('data:'.length).trim();
      return;
    }

    if (line.isEmpty) {
      yield* _emitBuffered();
    }
  }

  Iterable<BehaviorEvent> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<BehaviorEvent> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name != 'behavior') {
      return;
    }

    final event = _decode(data);
    if (event != null) {
      yield event;
    }
  }

  static BehaviorEvent? _decode(String payload) {
    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) {
        return null;
      }
      return BehaviorEvent.fromJson(Map<String, Object?>.from(decoded));
    } on FormatException {
      return null;
    } on TypeError {
      return null;
    }
  }
}
