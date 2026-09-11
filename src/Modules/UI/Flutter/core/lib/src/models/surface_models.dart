final class ActivateControlRequest {
  const ActivateControlRequest({required this.intent, this.surfaceKey});

  final String intent;
  final String? surfaceKey;

  Map<String, Object?> toJson() => {
    'intent': intent,
    if (surfaceKey != null) 'surfaceKey': surfaceKey,
  };
}

sealed class SurfaceStreamEvent {
  const SurfaceStreamEvent();
}

final class SurfaceSignalObserved extends SurfaceStreamEvent {
  const SurfaceSignalObserved({
    required this.sequence,
    required this.signal,
    required this.body,
  });

  final int sequence;
  final String signal;
  final Map<String, Object?> body;

  factory SurfaceSignalObserved.fromJson(Map<String, Object?> json) =>
      SurfaceSignalObserved(
        sequence: (json['sequence'] as num).toInt(),
        signal: json['signal'] as String,
        body: Map<String, Object?>.from(json['body'] as Map? ?? const {}),
      );
}

// Consumers re-read the surface, so only the cursor is retained from a reset.
final class SurfaceStreamReset extends SurfaceStreamEvent {
  const SurfaceStreamReset({required this.cursor});

  final int cursor;
}
