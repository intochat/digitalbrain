import 'dart:convert';

import 'package:digitalbrain_wire/digitalbrain_wire.dart';

/// Parses SSE text (including multi-event batches) into scene-opened projections.
Iterable<SceneOpenedEvent> parseSseSceneOpenedEvents(String text) sync* {
  String? dataLine;
  for (final line in const LineSplitter().convert(text)) {
    if (line.startsWith('data:')) {
      dataLine = line.substring('data:'.length).trim();
      continue;
    }

    if (line.isEmpty && dataLine != null) {
      final payload = dataLine;
      dataLine = null;
      final decoded = jsonDecode(payload);
      if (decoded is Map<String, dynamic>) {
        yield SceneOpenedEvent.fromJson(decoded.cast<String, Object?>());
      }
    }
  }

  if (dataLine != null) {
    final decoded = jsonDecode(dataLine);
    if (decoded is Map<String, dynamic>) {
      yield SceneOpenedEvent.fromJson(decoded.cast<String, Object?>());
    }
  }
}
