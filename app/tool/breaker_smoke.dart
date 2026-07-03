import 'dart:io';
import 'package:digitalbrain_flutter/telemetry/export_circuit_breaker.dart';

void main() {
  final breaker = ExportCircuitBreaker(maxFailures: 3);

  if (breaker.recordFailure()) throw StateError('disabled after 1 failure');
  if (breaker.recordFailure()) throw StateError('disabled after 2 failures');
  if (breaker.disabled) throw StateError('disabled before reset');

  breaker.recordSuccess();

  if (breaker.disabled) throw StateError('still disabled after success reset');

  if (breaker.recordFailure()) throw StateError('disabled after 1 failure post-reset');
  if (breaker.recordFailure()) throw StateError('disabled after 2 failures post-reset');
  final tripped = breaker.recordFailure();
  if (!tripped) throw StateError('not disabled after 3 failures');
  if (!breaker.disabled) throw StateError('disabled getter false after trip');

  breaker.recordSuccess();
  if (!breaker.disabled) throw StateError('trip must be permanent: breaker re-enabled after recordSuccess');

  stdout.writeln('OK');
}
