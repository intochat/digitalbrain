import 'package:flutter/widgets.dart';

import '../../runtime/runtime_session_owner.dart';

class AppSessionScope extends InheritedNotifier<RuntimeSessionOwner> {
  const AppSessionScope({
    super.key,
    required RuntimeSessionOwner owner,
    required super.child,
  }) : super(notifier: owner);

  static RuntimeSessionOwner of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppSessionScope>();
    if (scope == null) {
      throw StateError('DigitalBrain session scope is unavailable.');
    }
    return scope.notifier!;
  }
}
