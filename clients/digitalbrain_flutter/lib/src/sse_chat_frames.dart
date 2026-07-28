import 'dart:convert';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';

final class SseChatTurnParser {
  String? _dataLine;
  String? _eventName;

  Iterable<ChatTurnEvent> addLine(String line) sync* {
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

  Iterable<ChatTurnEvent> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<ChatTurnEvent> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name != 'chat-turn') {
      return;
    }

    final event = _decode(data);
    if (event != null) {
      yield event;
    }
  }

  static ChatTurnEvent? _decode(String payload) {
    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) {
        return null;
      }
      return ChatTurnEvent.fromJson(Map<String, Object?>.from(decoded));
    } on FormatException {
      return null;
    } on TypeError {
      return null;
    }
  }
}
