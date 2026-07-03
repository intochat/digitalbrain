import 'package:flutter/foundation.dart';

// Accumulator for InoSourceCard chunks. BrainSceneScreen routes inbound
// InoSourceCard envelopes here when their correlationId matches the editor's
// current authoring correlation. The CodeEditor (or a TypewriterController
// owned by the editor body) reads `accumulated` and types it.
class InoSourceSubscription extends ChangeNotifier {
  String? _correlationId;
  final StringBuffer _buffer = StringBuffer();

  String? get correlationId => _correlationId;
  String get accumulated => _buffer.toString();

  void open(String correlationId) {
    _correlationId = correlationId;
    _buffer.clear();
    notifyListeners();
  }

  void appendIfMatch(String cid, Iterable<String> chunks) {
    if (cid != _correlationId) return;
    for (final c in chunks) {
      _buffer.write(c);
    }
    notifyListeners();
  }

  void appendDirect(String text) {
    _buffer.write(text);
    notifyListeners();
  }

  void updateText(String text) {
    _buffer.clear();
    _buffer.write(text);
    notifyListeners();
  }

  void close() {
    _correlationId = null;
    _buffer.clear();
    notifyListeners();
  }
}
