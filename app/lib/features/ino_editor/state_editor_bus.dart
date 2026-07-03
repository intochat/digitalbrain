import 'package:flutter/foundation.dart';

// Side-channel bus for the StateEditor widget. RFW's event model does not
// allow passing dynamic values (like user-edited inputs) inside event parameters,
// so when the user edits a state variable, the stateful editor pushes the update
// here, and the host screen consumes it from the event handler callback.
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
