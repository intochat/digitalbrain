import 'dart:convert';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';

final class SseChatDeltaParser {
  String? _dataLine;
  String? _eventName;

  Iterable<ChatDelta> addLine(String line) sync* {
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

  Iterable<ChatDelta> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<ChatDelta> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name != 'chat-delta') {
      return;
    }

    final event = _decode(data);
    if (event != null) {
      yield event;
    }
  }

  static ChatDelta? _decode(String payload) {
    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) {
        return null;
      }
      return ChatDelta.fromJson(Map<String, Object?>.from(decoded));
    } on FormatException {
      return null;
    } on TypeError {
      return null;
    }
  }
}
