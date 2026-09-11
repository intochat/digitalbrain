import 'dart:convert';

import 'ui_models.dart';

abstract base class SseFrameParser<T> {
  String? _dataLine;
  String? _eventName;

  Iterable<T> addLine(String line) sync* {
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

  Iterable<T> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<T> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    yield* decode(name ?? '', data);
  }

  Iterable<T> decode(String eventName, String data);
}

final class SseSurfaceEventParser extends SseFrameParser<SurfaceStreamEvent> {
  @override
  Iterable<SurfaceStreamEvent> decode(String eventName, String data) sync* {
    if (eventName != 'surface' && eventName != 'reset') return;
    try {
      final decoded = jsonDecode(data);
      if (decoded is! Map) return;
      if (eventName == 'reset') {
        yield SurfaceStreamReset(cursor: (decoded['cursor'] as num).toInt());
      } else {
        yield SurfaceSignalObserved.fromJson(
          Map<String, Object?>.from(decoded),
        );
      }
    } on FormatException {
      return;
    } on TypeError {
      return;
    }
  }
}
