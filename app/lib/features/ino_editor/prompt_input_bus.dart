import 'package:flutter/foundation.dart';

// Single-mount side channel for the prompt-input widget. RFW's event model
// fires events with rfwtxt-declared args, not Dart-side state, so the user's
// typed text can't ride the 'onSubmit' event payload directly. The PromptInput
// native widget pushes every text change here; BrainSceneScreen reads it from
// its 'onSubmit' event handler. Single open editor at a time.
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
