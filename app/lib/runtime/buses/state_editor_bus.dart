import 'package:flutter/foundation.dart';

class StateEditorBus extends ChangeNotifier {
  StateEditorBus._();
  static final StateEditorBus instance = StateEditorBus._();

  String _lastEditedKey = '';
  dynamic _lastEditedValue;

  String get lastEditedKey => _lastEditedKey;
  dynamic get lastEditedValue => _lastEditedValue;

  void set(String key, dynamic value) {
    _lastEditedKey = key;
    _lastEditedValue = value;
    notifyListeners();
  }

  void clear() {
    _lastEditedKey = '';
    _lastEditedValue = null;
    notifyListeners();
  }
}
