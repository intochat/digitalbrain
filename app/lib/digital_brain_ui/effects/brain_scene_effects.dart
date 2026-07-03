import 'package:flutter/widgets.dart';

import 'effects_pulse.dart';

class BrainSceneEffects extends ChangeNotifier {
  final List<EffectsPulse> _pulses = [];
  Set<String> _activeCorrelations = const {};

  List<EffectsPulse> get pulses => List.unmodifiable(_pulses);
  Set<String> get activeCorrelations => _activeCorrelations;

  void fire(EffectsPulse pulse) {
    _pulses.add(pulse);
    notifyListeners();
  }

  void sweep(DateTime now) {
    final before = _pulses.length;
    _pulses.removeWhere((p) => p.isExpired(now));
    if (_pulses.length != before) notifyListeners();
  }

  void setActiveCorrelations(Set<String> ids) {
    if (_setEquals(_activeCorrelations, ids)) return;
    _activeCorrelations = Set.unmodifiable(ids);
    notifyListeners();
  }
}

class BrainSceneEffectsScope extends InheritedNotifier<BrainSceneEffects> {
  const BrainSceneEffectsScope({
    required super.notifier,
    required super.child,
    super.key,
  });

  static BrainSceneEffects of(BuildContext context) {
    final scope = context
        .dependOnInheritedWidgetOfExactType<BrainSceneEffectsScope>();
    assert(
      scope?.notifier != null,
      'No BrainSceneEffectsScope above this widget.',
    );
    return scope!.notifier!;
  }

  static BrainSceneEffects? maybeOf(BuildContext context) => context
      .dependOnInheritedWidgetOfExactType<BrainSceneEffectsScope>()
      ?.notifier;
}

bool _setEquals<T>(Set<T> a, Set<T> b) =>
    a.length == b.length && a.containsAll(b);
