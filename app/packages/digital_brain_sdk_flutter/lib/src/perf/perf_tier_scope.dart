import 'package:flutter/widgets.dart';
import 'perf_tier_controller.dart';

class PerfTierScope extends InheritedNotifier<PerfTierController> {
  const PerfTierScope({
    required super.notifier,
    required super.child,
    super.key,
  });

  static PerfTierController of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<PerfTierScope>();
    assert(scope?.notifier != null, 'No PerfTierScope above this widget.');
    return scope!.notifier!;
  }

  static PerfTierController? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<PerfTierScope>()?.notifier;
}
