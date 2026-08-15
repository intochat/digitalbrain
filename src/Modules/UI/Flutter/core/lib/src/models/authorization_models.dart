final class AuthorizationEvent {
  const AuthorizationEvent({
    required this.sequence,
    required this.kind,
    required this.commandId,
    required this.serverKey,
    this.serverDisplayName,
    this.signInUrl,
    required this.state,
    required this.timestamp,
  });

  final int sequence;
  final String kind;
  final String commandId;
  final String serverKey;
  final String? serverDisplayName;
  final String? signInUrl;
  final String state;
  final DateTime timestamp;

  bool get isRequired => kind == 'AuthorizationRequired';
  bool get isCompleted => kind == 'AuthorizationCompleted';
  bool get isDenied => kind == 'AuthorizationDenied';
  bool get isResolved => isCompleted || isDenied;

  factory AuthorizationEvent.fromJson(Map<String, Object?> json) {
    return AuthorizationEvent(
      sequence: (json['sequence'] as num).toInt(),
      kind: json['kind'] as String,
      commandId: json['commandId'] as String,
      serverKey: json['serverKey'] as String,
      serverDisplayName: json['serverDisplayName'] as String?,
      signInUrl: json['signInUrl'] as String?,
      state: json['state'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
    );
  }
}

/// Pending sign-in cards rebuilt from authorization journal facts.

final class SignInCardProjection {
  const SignInCardProjection({
    required this.state,
    required this.commandId,
    required this.serverKey,
    required this.serverDisplayName,
    required this.signInUrl,
  });

  final String state;
  final String commandId;
  final String serverKey;
  final String serverDisplayName;
  final Uri signInUrl;

  static List<SignInCardProjection> project(
    Iterable<AuthorizationEvent> events,
  ) {
    final open = <String, SignInCardProjection>{};
    final ordered = events.toList()
      ..sort((a, b) => a.sequence.compareTo(b.sequence));

    for (final event in ordered) {
      if (event.isRequired) {
        final url = event.signInUrl;
        final name = event.serverDisplayName;
        if (url == null || name == null || name.isEmpty) {
          continue;
        }
        open[event.state] = SignInCardProjection(
          state: event.state,
          commandId: event.commandId,
          serverKey: event.serverKey,
          serverDisplayName: name,
          signInUrl: Uri.parse(url),
        );
        continue;
      }

      if (event.isResolved) {
        open.remove(event.state);
      }
    }

    return List<SignInCardProjection>.unmodifiable(open.values);
  }
}
