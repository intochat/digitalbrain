import 'package:flutter/material.dart';
import 'package:rfw/formats.dart' show parseLibraryFile;
import 'package:rfw/rfw.dart';

import 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

/// One process-wide RFW runtime: the host-owned `digitalbrain` dictionary plus
/// per-key parsed document libraries. Parse failures are captured per key so a
/// bad document degrades to an error string instead of taking down the host.
class RfwRuntimeHost {
  RfwRuntimeHost() {
    _runtime.update(const LibraryName(['digitalbrain']), createDigitalBrainWidgets());
  }

  final Runtime _runtime = Runtime();
  final Set<String> _loaded = <String>{};
  final Map<String, String> _parseErrors = <String, String>{};

  /// Parse `source` once under library name `['doc', key]`. Idempotent.
  void ensureLoaded(String key, String source) {
    if (_loaded.contains(key) || _parseErrors.containsKey(key)) return;
    // Normalize CRLF / CR to LF before parsing. The RFW parser rejects U+000D
    // (carriage return) — networked sources sometimes carry CRLF line endings
    // that the parser would reject at column 16 of line 1.
    final normalized = source.replaceAll('\r\n', '\n').replaceAll('\r', '\n');
    try {
      _runtime.update(LibraryName(['doc', key]), parseLibraryFile(normalized));
      _loaded.add(key);
    } catch (e) {
      _parseErrors[key] = e.toString();
    }
  }

  String? parseError(String key) => _parseErrors[key];

  /// Check if a document key has already been successfully loaded.
  bool isLoaded(String key) => _loaded.contains(key);

  Widget render(
    String key, {
    required Map<String, Object?> data,
    required RemoteEventHandler onEvent,
    String rootWidget = 'root',
  }) {
    return RemoteWidget(
      runtime: _runtime,
      data: DynamicContent(data),
      widget: FullyQualifiedWidgetName(LibraryName(['doc', key]), rootWidget),
      onEvent: onEvent,
    );
  }
}
