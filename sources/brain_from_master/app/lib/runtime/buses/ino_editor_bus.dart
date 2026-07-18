import 'package:flutter/foundation.dart';
import 'ino_source_subscription.dart';

class InoEditorBus extends ChangeNotifier {
  InoEditorBus._privateConstructor();
  static final InoEditorBus instance = InoEditorBus._privateConstructor();

  InoSourceSubscription? _activeSubscription;

  InoSourceSubscription? get activeSubscription => _activeSubscription;

  set activeSubscription(InoSourceSubscription? subscription) {
    if (_activeSubscription != subscription) {
      _activeSubscription = subscription;
      notifyListeners();
    }
  }
}
