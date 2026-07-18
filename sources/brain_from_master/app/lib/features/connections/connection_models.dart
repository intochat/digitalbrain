enum ConnectionHealth {
  healthy('Healthy'),
  needsReauth('Needs reauth'),
  disconnected('Disconnected'),
  misconfigured('Misconfigured');

  const ConnectionHealth(this.label);

  final String label;

  bool get isHealthy => this == ConnectionHealth.healthy;
}

class ConnectionItem {
  ConnectionItem({
    required this.provider,
    required this.connectionId,
    required this.displayName,
    required this.health,
    required this.healthDetail,
    required this.unlockedCapabilityIds,
    required this.connectPath,
  }) {
    _requireIdentity(provider, 'provider', 128);
    _requireIdentity(connectionId, 'connectionId', 256);
    _requireText(displayName, 'displayName', 256);
    _requireOptionalText(healthDetail, 'healthDetail', 1000);
    for (final capabilityId in unlockedCapabilityIds) {
      _requireIdentity(capabilityId, 'unlockedCapabilityIds', 256);
    }
    _requireOptionalConnectPath(connectPath);
  }

  final String provider;
  final String connectionId;
  final String displayName;
  final ConnectionHealth health;
  final String? healthDetail;
  final List<String> unlockedCapabilityIds;
  final String? connectPath;

  bool get canConnect =>
      connectPath != null && connectPath!.isNotEmpty && !health.isHealthy;

  List<String> get visibleCapabilityIds =>
      health.isHealthy ? unlockedCapabilityIds : const <String>[];
}

void _requireIdentity(String value, String name, int maximumLength) {
  if (!_isBoundedSafeText(value, maximumLength)) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}

void _requireText(String value, String name, int maximumLength) {
  if (!_isBoundedSafeText(value, maximumLength)) {
    throw ArgumentError.value(value, name, 'Invalid text.');
  }
}

void _requireOptionalText(String? value, String name, int maximumLength) {
  if (value != null) _requireText(value, name, maximumLength);
}

void _requireOptionalConnectPath(String? value) {
  if (value == null) return;
  if (value.isEmpty ||
      value.length > 1024 ||
      !value.startsWith('/') ||
      value.contains('://') ||
      value.contains(' ') ||
      value.runes.any(
        (character) => character < 32 || character >= 127 && character <= 159,
      )) {
    throw ArgumentError.value(value, 'connectPath', 'Invalid connect path.');
  }
}

bool _isBoundedSafeText(String value, int maximumLength) =>
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value.trim() == value &&
    !value.runes.any(
      (character) => character < 32 || character >= 127 && character <= 159,
    );
