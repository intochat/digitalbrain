enum TransportErrorCode {
  cancelled,
  unauthenticated,
  permissionDenied,
  invalidArgument,
  unavailable,
  protocol,
  unknown,
}

class TransportException implements Exception {
  const TransportException(this.code, this.safeMessage);

  final TransportErrorCode code;
  final String safeMessage;

  bool get isTerminal => switch (code) {
    TransportErrorCode.permissionDenied ||
    TransportErrorCode.invalidArgument ||
    TransportErrorCode.protocol => true,
    _ => false,
  };

  @override
  String toString() => safeMessage;
}

class AuthenticationException extends TransportException {
  const AuthenticationException([
    String message = 'Authenticated runtime session required.',
  ]) : super(TransportErrorCode.unauthenticated, message);
}

class ProtocolException extends TransportException {
  const ProtocolException(String message)
    : super(TransportErrorCode.protocol, message);
}

class ScopeViolation extends ProtocolException {
  const ScopeViolation(super.message);
}
