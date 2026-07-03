/// Maps RFW `event "name" { ...args }` to a host capability. Pure: no Flutter
/// imports, so it is `dart test`-able as a unit (CLAUDE.md forbids `flutter
/// test`, not pure-Dart libraries consumed by E2E).
typedef Capability = void Function(Map<String, Object?> args);

class EventTable {
  EventTable(this._caps);
  final Map<String, Capability> _caps;

  /// Returns true if the event was handled by a registered capability.
  bool dispatch(String name, Map<String, Object?> args) {
    final cap = _caps[name];
    if (cap == null) return false;
    cap(args);
    return true;
  }

  bool has(String name) => _caps.containsKey(name);
}
