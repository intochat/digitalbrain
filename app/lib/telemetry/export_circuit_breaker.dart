class ExportCircuitBreaker {
  ExportCircuitBreaker({this.maxFailures = 3});

  final int maxFailures;
  int _consecutiveFailures = 0;
  bool _disabled = false;

  bool get disabled => _disabled;

  void recordSuccess() => _consecutiveFailures = 0;

  bool recordFailure() {
    if (++_consecutiveFailures >= maxFailures) {
      _disabled = true;
    }
    return _disabled;
  }
}
