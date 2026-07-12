import 'dart:async';

import 'package:flutter/widgets.dart';

class TypewriterController extends ChangeNotifier {
  TypewriterController({this.stepMs = 18});
  final int stepMs;

  final StringBuffer _buffer = StringBuffer();
  String _shown = '';
  Timer? _timer;
  bool _disposed = false;

  String get shown => _shown;

  void appendChunk(String chunk) {
    if (_disposed) return;
    _buffer.write(chunk);
    _ensureRunning();
  }

  void cutToEnd() {
    if (_disposed) return;
    _shown = _buffer.toString();
    _stop();
    notifyListeners();
  }

  void _ensureRunning() {
    if (_timer != null) return;
    _timer = Timer.periodic(Duration(milliseconds: stepMs), (_) {
      if (_disposed) return;
      final full = _buffer.toString();
      if (_shown.length >= full.length) {
        _stop();
        return;
      }
      _shown = full.substring(0, _shown.length + 1);
      notifyListeners();
    });
  }

  void _stop() {
    _timer?.cancel();
    _timer = null;
  }

  @override
  void dispose() {
    _disposed = true;
    _stop();
    super.dispose();
  }
}
