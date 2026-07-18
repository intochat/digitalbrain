import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:digitalbrain_flutter/telemetry/export_circuit_breaker.dart';

class SimpleCounter {
  SimpleCounter({
    required this.name,
    this.unit = '',
    this.description = '',
  });

  final String name;
  final String unit;
  final String description;
  final Map<String, int> _points = {};

  void add(int value, {Map<String, String> attributes = const {}}) {
    final key = _attrsKey(attributes);
    _points[key] = (_points[key] ?? 0) + value;
    _latestAttrs[key] = attributes;
  }

  final Map<String, Map<String, String>> _latestAttrs = {};

  String _attrsKey(Map<String, String> attrs) {
    final entries = attrs.entries.toList()..sort((a, b) => a.key.compareTo(b.key));
    return entries.map((e) => '${e.key}=${e.value}').join(',');
  }
}

class SimpleHistogram {
  SimpleHistogram({
    required this.name,
    this.unit = '',
    this.description = '',
  });

  final String name;
  final String unit;
  final String description;
  final Map<String, _HistogramBucket> _buckets = {};
  final Map<String, Map<String, String>> _latestAttrs = {};

  void record(double value, {Map<String, String> attributes = const {}}) {
    final key = _attrsKey(attributes);
    final bucket = _buckets.putIfAbsent(key, _HistogramBucket.new);
    bucket.count++;
    bucket.sum += value;
    if (value < bucket.min) bucket.min = value;
    if (value > bucket.max) bucket.max = value;
    _latestAttrs[key] = attributes;
  }

  String _attrsKey(Map<String, String> attrs) {
    final entries = attrs.entries.toList()..sort((a, b) => a.key.compareTo(b.key));
    return entries.map((e) => '${e.key}=${e.value}').join(',');
  }
}

class _HistogramBucket {
  int count = 0;
  double sum = 0;
  double min = double.infinity;
  double max = double.negativeInfinity;
}

class OtlpMetricExporter {
  OtlpMetricExporter({
    required String endpoint,
    required this.serviceName,
    this.serviceVersion = '0.1.0',
    this.headers = const {},
    Duration exportInterval = const Duration(seconds: 15),
  }) : _endpoint = endpoint {
    _startNano = DateTime.now().microsecondsSinceEpoch * 1000;
    _timer = Timer.periodic(exportInterval, (_) => flush());
  }

  final String _endpoint;
  final String serviceName;
  final Map<String, String> headers;
  final String serviceVersion;
  final List<SimpleCounter> _counters = [];
  final List<SimpleHistogram> _histograms = [];
  late final Timer _timer;
  late final int _startNano;

  final _breaker = ExportCircuitBreaker();

  SimpleCounter createCounter(String name, {String unit = '', String description = ''}) {
    final c = SimpleCounter(name: name, unit: unit, description: description);
    _counters.add(c);
    return c;
  }

  SimpleHistogram createHistogram(String name, {String unit = '', String description = ''}) {
    final h = SimpleHistogram(name: name, unit: unit, description: description);
    _histograms.add(h);
    return h;
  }

  Future<void> flush() async {
    if (_breaker.disabled) return;
    final nowNano = DateTime.now().microsecondsSinceEpoch * 1000;
    final metrics = <Map<String, dynamic>>[];

    for (final c in _counters) {
      if (c._points.isEmpty) continue;
      metrics.add({
        'name': c.name,
        'unit': c.unit,
        'description': c.description,
        'sum': {
          'dataPoints': c._points.entries.map((e) {
            final attrs = c._latestAttrs[e.key] ?? {};
            return {
              'asInt': e.value.toString(),
              'startTimeUnixNano': _startNano.toString(),
              'timeUnixNano': nowNano.toString(),
              if (attrs.isNotEmpty)
                'attributes': attrs.entries
                    .map((a) => _attr(a.key, a.value))
                    .toList(),
            };
          }).toList(),
          'aggregationTemporality': 2,
          'isMonotonic': true,
        },
      });
    }

    for (final h in _histograms) {
      if (h._buckets.isEmpty) continue;
      metrics.add({
        'name': h.name,
        'unit': h.unit,
        'description': h.description,
        'histogram': {
          'dataPoints': h._buckets.entries.map((e) {
            final bucket = e.value;
            final attrs = h._latestAttrs[e.key] ?? {};
            return {
              'count': bucket.count.toString(),
              'sum': bucket.sum,
              'min': bucket.min == double.infinity ? 0 : bucket.min,
              'max': bucket.max == double.negativeInfinity ? 0 : bucket.max,
              'startTimeUnixNano': _startNano.toString(),
              'timeUnixNano': nowNano.toString(),
              if (attrs.isNotEmpty)
                'attributes': attrs.entries
                    .map((a) => _attr(a.key, a.value))
                    .toList(),
            };
          }).toList(),
          'aggregationTemporality': 2,
        },
      });
    }

    if (metrics.isEmpty) return;

    final payload = {
      'resourceMetrics': [
        {
          'resource': {
            'attributes': [
              _attr('service.name', serviceName),
              _attr('service.version', serviceVersion),
              _attr('telemetry.sdk.language', 'dart'),
            ],
          },
          'scopeMetrics': [
            {
              'scope': {'name': serviceName, 'version': serviceVersion},
              'metrics': metrics,
            }
          ],
        }
      ],
    };

    try {
      await http
          .post(
            Uri.parse(_endpoint),
            headers: {'Content-Type': 'application/json', ...headers},
            body: jsonEncode(payload),
          )
          .timeout(const Duration(seconds: 4));
      _breaker.recordSuccess();
    } catch (_) {
      if (_breaker.recordFailure()) {
        _timer.cancel();
        _counters.clear();
        _histograms.clear();
      }
    }
  }

  Map<String, dynamic> _attr(String key, String value) {
    return {
      'key': key,
      'value': {'stringValue': value},
    };
  }

  Future<void> shutdown() async {
    _timer.cancel();
    await flush();
  }
}
