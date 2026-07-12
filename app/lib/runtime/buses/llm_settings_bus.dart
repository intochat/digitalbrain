import 'package:flutter/foundation.dart';

// Side-channel bus for the LlmSettingsPanel widget. Safely transfers updated
// LLM parameters (model, temperature, max attempts) and visual preferences
// (replaceSpheresWithIcons, showSynapses) across RFW boundaries to the host screen.
class LlmSettingsBus extends ChangeNotifier {
  LlmSettingsBus._();
  static final LlmSettingsBus instance = LlmSettingsBus._();

  String _model = 'GPT-4o';
  double _temp = 0.7;
  int _attempts = 3;
  bool _replaceSpheresWithIcons = false;
  bool _showSynapses = false;
  bool _localAiMode = false;

  String get model => _model;
  double get temp => _temp;
  int get attempts => _attempts;
  bool get replaceSpheresWithIcons => _replaceSpheresWithIcons;
  bool get showSynapses => _showSynapses;
  bool get localAiMode => _localAiMode;

  void update(String model, double temp, int attempts, bool replaceSpheresWithIcons, bool showSynapses, bool localAiMode) {
    _model = model;
    _temp = temp;
    _attempts = attempts;
    _replaceSpheresWithIcons = replaceSpheresWithIcons;
    _showSynapses = showSynapses;
    _localAiMode = localAiMode;
    notifyListeners();
  }

  void clear() {
    _model = 'GPT-4o';
    _temp = 0.7;
    _attempts = 3;
    _replaceSpheresWithIcons = false;
    _showSynapses = false;
    _localAiMode = false;
    notifyListeners();
  }
}
