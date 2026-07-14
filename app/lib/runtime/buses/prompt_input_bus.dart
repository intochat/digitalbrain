import 'package:flutter/foundation.dart';

class PromptInputBus extends ChangeNotifier {
  PromptInputBus._();
  static final PromptInputBus instance = PromptInputBus._();

  String _text = '';
  String get text => _text;

  void set(String value) {
    if (_text == value) return;
    _text = value;
    notifyListeners();
  }

  void clear() {
    if (_text.isEmpty) return;
    _text = '';
    notifyListeners();
  }
}
