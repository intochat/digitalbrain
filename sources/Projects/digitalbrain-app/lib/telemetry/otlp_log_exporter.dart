import 'dart:async';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:digitalbrain_flutter/telemetry/export_circuit_breaker.dart';

enum Severity {
  trace(1, 'TRACE'),
  debug(5, 'DEBUG'),
  info(9, 'INFO'),
  warn(13, 'WARN'),
  error(17, 'ERROR'),
  fatal(21, 'FATAL');

  const Severity(this.number, this.text);
  final int number;
  final String text;
}

class LogRecord {
  LogRecord({
    required this.severity,
    required this.body,
    this.attributes = const {},
    DateTime? timestamp,
  }) : timestampNano = (timestamp ?? DateTime.now())
            .microsecondsSinceEpoch *
        1000;

  final Severity severity;
  final String body;
  final Map<String, String> attributes;
  final int timestampNano;
}

class OtlpLogExporter {
  OtlpLogExporter({
    required String endpoint,
    required this.serviceName,
    this.serviceVersion = '0.1.0',
    this.headers = const {},
    Duration flushInterval = const Duration(seconds: 5),
    this.maxBatchSize = 512,
  }) : _endpoint = endpoint {
    _timer = Timer.periodic(flushInterval, (_) => flush());
  }

  final String _endpoint;
  final String serviceName;
  final String serviceVersion;
  final Map<String, String> headers;
  final int maxBatchSize;
  final List<LogRecord> _buffer = [];
  late final Timer _timer;

  final _breaker = ExportCircuitBreaker();

  void emit(LogRecord record) {
    if (_breaker.disabled) return;
    _buffer.add(record);
    if (_buffer.length >= maxBatchSize) flush();
  }

  Future<void> flush() async {
    if (_breaker.disabled || _buffer.isEmpty) return;
    final batch = List<LogRecord>.from(_buffer);
    _buffer.clear();

    final payload = {
      'resourceLogs': [
        {
          'resource': {
            'attributes': [
              _attr('service.name', serviceName),
              _attr('service.version', serviceVersion),
              _attr('telemetry.sdk.language', 'dart'),
            ],
          },
          'scopeLogs': [
            {
              'scope': {'name': serviceName, 'version': serviceVersion},
              'logRecords': batch.map(_recordToJson).toList(),
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
        _buffer.clear();
      }
    }
  }

  Map<String, dynamic> _recordToJson(LogRecord r) {
    return {
      'timeUnixNano': r.timestampNano.toString(),
      'severityNumber': r.severity.number,
      'severityText': r.severity.text,
      'body': {'stringValue': r.body},
      if (r.attributes.isNotEmpty)
        'attributes': r.attributes.entries.map((e) => _attr(e.key, e.value)).toList(),
    };
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

class OtlpLogger {
  OtlpLogger(this._exporter);
  final OtlpLogExporter _exporter;

  void trace(String msg, {Map<String, String> attrs = const {}}) =>
      _exporter.emit(LogRecord(severity: Severity.trace, body: msg, attributes: attrs));

  void debug(String msg, {Map<String, String> attrs = const {}}) =>
      _exporter.emit(LogRecord(severity: Severity.debug, body: msg, attributes: attrs));

  void info(String msg, {Map<String, String> attrs = const {}}) =>
      _exporter.emit(LogRecord(severity: Severity.info, body: msg, attributes: attrs));

  void warn(String msg, {Map<String, String> attrs = const {}}) =>
      _exporter.emit(LogRecord(severity: Severity.warn, body: msg, attributes: attrs));

  void error(String msg, {Map<String, String> attrs = const {}}) =>
      _exporter.emit(LogRecord(severity: Severity.error, body: msg, attributes: attrs));
}
