import 'package:flutter/widgets.dart';

class UiKitFormController extends ChangeNotifier {
  final Map<String, String> _values = {};

  Map<String, String> get values => Map.unmodifiable(_values);

  void set(String name, String value) {
    _values[name] = value;
    notifyListeners();
  }
}

class UiKitFormScope extends InheritedNotifier<UiKitFormController> {
  const UiKitFormScope({
    required UiKitFormController controller,
    required super.child,
    super.key,
  }) : super(notifier: controller);

  static UiKitFormController? of(BuildContext context) =>
      context
          .dependOnInheritedWidgetOfExactType<UiKitFormScope>()
          ?.notifier;
}
